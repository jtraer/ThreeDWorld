﻿using UnityEngine;
using System.Collections;
using NetMQ;

public abstract class AbstractInputModule
{
    #region Fields
    protected Avatar _myAvatar = null;
    const double DOUBLE_CONVERSION = 1 << 30;
    const int VECTOR_ELEMENT_SHIFT = 1 << 10;
    const int VECTOR_ELEMENT_MASK = (1 << 10)-1;
    #endregion

    public AbstractInputModule(Avatar myAvatar)
    {
        _myAvatar = myAvatar;
    }
    
    public abstract void SimulateInputFromController(ref NetMQMessage newMsg);
    
    // Parse the input sent from the client and use it to update the controls for the next simulation segment
    public abstract void HandleNetInput(NetMQMessage msg, ref Vector3 targetVel);

#region Encoding/Decoding
    protected static void EncodeFloat01(float val, NetMQMessage msg)
    {
        EncodeDouble01((double) val, msg);
    }
    protected static void EncodeDouble01(double val, NetMQMessage msg)
    {
        int ret = (int)System.Math.Round(val * DOUBLE_CONVERSION);
        msg.Append(ret);
    }
    protected static float ReadFloat01(NetMQMessage msg, ref int curIndex)
    {
        return (float)ReadDouble01(msg, ref curIndex);
    }
    protected static double ReadDouble01(NetMQMessage msg, ref int curIndex)
    {
        if (msg.FrameCount <= curIndex)
            return 0.0f;
        return msg[curIndex++].ConvertToInt32() / DOUBLE_CONVERSION;
    }
    protected static void EncodeVector01(Vector3 val, NetMQMessage msg)
    {
        EncodeFloat01(val.x, msg);
        EncodeFloat01(val.y, msg);
        EncodeFloat01(val.z, msg);
    }
    protected static Vector3 ReadVector01(NetMQMessage msg, ref int curIndex)
    {
        Vector3 ret = Vector3.zero;
        ret.x = ReadFloat01(msg, ref curIndex);
        ret.y = ReadFloat01(msg, ref curIndex);
        ret.z = ReadFloat01(msg, ref curIndex);
        return ret;
    }
#endregion
};

public class InputModule : AbstractInputModule
{
    public InputModule(Avatar myAvatar) : base(myAvatar) {}

    public override void SimulateInputFromController(ref NetMQMessage newMsg)
    {
        // Set movement
        Quaternion curRotation = _myAvatar.transform.rotation;
        Vector3 targetVelocity = Vector3.zero;
        targetVelocity.x = Input.GetAxis("Horizontal");
        targetVelocity.y = Input.GetAxis("Vertical");
        targetVelocity.z = Input.GetAxis("VerticalD");
        targetVelocity = curRotation * targetVelocity;
        
        // Read angular velocity
        Vector3 targetRotationVel = Vector3.zero;
        targetRotationVel.x = -Input.GetAxis("Vertical2");
        targetRotationVel.y = Input.GetAxis("Horizontal2");
        targetRotationVel.z = -Input.GetAxis("HorizontalD");
        
        // Convert from relative coordinates
        Quaternion test = Quaternion.identity;
        test = test * Quaternion.AngleAxis(targetRotationVel.z, curRotation * Vector3.forward);
        test = test * Quaternion.AngleAxis(targetRotationVel.x, curRotation * Vector3.left);
        test = test * Quaternion.AngleAxis(targetRotationVel.y, curRotation * Vector3.up);
        targetRotationVel = test.eulerAngles;
        
        EncodeVector01(targetVelocity, newMsg);
        EncodeVector01(targetRotationVel, newMsg);
    }

    // Parse the input sent from the client and use it to update the controls for the next simulation segment
    public override void HandleNetInput(NetMQMessage msg, ref Vector3 targetVel)
    {
        Rigidbody myRigidbody = _myAvatar.myRigidbody;
        float moveSpeed = _myAvatar.moveSpeed;
        float rotSpeed = _myAvatar.rotSpeed;
        int curIndex = 1;
        // Get movement
        targetVel = moveSpeed * ReadVector01(msg, ref curIndex);// / 4096.0f) * new Vector3(msg[1].ConvertToInt32(), msg[2].ConvertToInt32(), msg[3].ConvertToInt32());

        Vector3 angChange = rotSpeed * ReadVector01(msg, ref curIndex);
        angChange -= myRigidbody.angularVelocity;
        // Clamp value to max change?
        if (angChange.magnitude > rotSpeed)
            angChange = angChange.normalized * rotSpeed;
        myRigidbody.AddTorque(angChange, ForceMode.VelocityChange);
    }
}
