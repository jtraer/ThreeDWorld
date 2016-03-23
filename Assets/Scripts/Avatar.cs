﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using NetMQ;

/// <summary>
/// Controls a single avatar driven by a connection to a client
/// </summary>
public class Avatar : MonoBehaviour
{
#region Fields
    // Reference to camera component we are streaming from. TODO: Change to a list to allow multiple cameras?
    public CameraStreamer myCam = null;
    // Reference to all the shaders we are using with each camera(null uses standard rendering)
    public List<Shader> shaders = null;
    // How fast the avatar can translate using the client controls
    public float moveSpeed = 5.0f;
    // How fast the avatar can rotate using the client controls
    public float rotSpeed = 5.0f;
    // The range for which this avatar can observe SemanticObject's
    public float observedRange = 25.0f;
    // send scene info?
	public bool sendSceneInfo = false;

    private AbstractInputModule _myInput = null;
    private List<SemanticObject> _observedObjs = new List<SemanticObject>();
    private Vector3 _targetVelocity = Vector3.zero;
    private Rigidbody _myRigidbody = null;
    private bool _readyForSimulation = false;
    private NetMessenger _myMessenger = null;
    private NetMQ.Sockets.ResponseSocket _myServer = null;
    private CameraStreamer.CaptureRequest _request;
    private bool _shouldCollectObjectInfo = true;
    private List<string> _relationshipsToRetrieve = new List<string>();
#endregion

#region Properties
    // The rigidbody associated with the avatar's body
    public Rigidbody myRigidbody {
        get {
            if (_myRigidbody == null)
                _myRigidbody = gameObject.GetComponent<Rigidbody>();
            return _myRigidbody;
        }
    }
    
    // Indicates when the client for this avatar is awaiting running the next set of frames
    public bool readyForSimulation {
        get { return _readyForSimulation; }
        set { _readyForSimulation = value; }
    }
    
    // The server-to-client connection associated with this avatar
    public NetMQ.Sockets.ResponseSocket myServer {
        get { return _myServer; }
    }
    
    // Objects found within observation radius of this avatar
    public List<SemanticObject> observedObjs {
        get { return _observedObjs; }
    }

    // Simulates input with a controller and interprets input messages
    public AbstractInputModule myInput {
        get { return _myInput; }
    }

    public bool shouldCollectObjectInfo {
        get { return _shouldCollectObjectInfo; }
        set { _shouldCollectObjectInfo = value; }
    }

    public List<string> relationshipsToRetrieve {
        get { return _relationshipsToRetrieve; }
        set { _relationshipsToRetrieve = value; }
    }
#endregion

#region Unity callbacks
    private void Awake()
    {
        _request = new CameraStreamer.CaptureRequest();
        _request.shadersList = shaders;
        _request.capturedImages = new List<CameraStreamer.CapturedImage>();
        _myInput = new InputModule(this);
        TeleportToValidPosition();
    }

    private void FixedUpdate()
    {
        if(myRigidbody != null)
        {
            myInput.OnFixedUpdate();
        }
    }
#endregion

    public void ReadyFramesForRequest()
    {
        // Set up rendering
        myCam.RequestCaptures(_request);
    }

    // Looks for all the SemanticObject's that are within the range
    public void UpdateObservedObjects()
    {
        _observedObjs.Clear();
        if (!_shouldCollectObjectInfo)
            return;
        if (NetMessenger.logTimingInfo)
            Debug.LogFormat("Starting Avatar.UpdateObservedObjects() {0}", Utils.GetTimeStamp());
        Collider[] observedObjects = Physics.OverlapSphere(transform.position, observedRange);
        if (NetMessenger.logTimingInfo)
            Debug.LogFormat("Finished OverlapSphere() and found {1}, {0}", Utils.GetTimeStamp(), observedObjects.Length);

        foreach(Collider col in observedObjects)
        {
            SemanticObjectSimple obj = null;
            if (col.attachedRigidbody == null)
                Debug.LogWarningFormat("{0} Collider doesn't have an attached rigidbody!", col.name);
            else
            {
                obj = col.attachedRigidbody.GetComponent<SemanticObjectSimple>();
                if (obj == null)
                    Debug.LogWarningFormat("{0} Rigidbody doesn't have an associated SemanticObject!", col.attachedRigidbody.name);
            }
            if (obj != null && !_observedObjs.Contains(obj))
            {
                _observedObjs.Add(obj);
                foreach(SemanticObjectComplex parentObj in obj.GetParentObjects())
                {
                    if (!_observedObjs.Contains(parentObj))
                        _observedObjs.Add(parentObj);
                }
            }
        }
        if (NetMessenger.logTimingInfo)
            Debug.LogFormat("Finished Avatar.UpdateObservedObjects() and found {1} {0}", Utils.GetTimeStamp(), observedObjs.Count);
    }

    public void TeleportToValidPosition()
    {
        // TODO: Have this check for more than a simple sphere of radius 0.5f for when we extend the avatar
        const float radius = 0.5f;
        Vector3 roomDim = ProceduralGeneration.Instance.roomDim;
        int xDim = Mathf.FloorToInt(roomDim.x) - 1, zDim = Mathf.FloorToInt(roomDim.z) - 1;
        float startHeight = (1.1f * radius);
        for (int i = 0; i < 1000; ++i)
        {
            Vector3 spawnTest = new Vector3(radius + Random.Range(0, xDim), startHeight, radius + Random.Range(0, zDim));
            if (!Physics.CheckSphere(spawnTest, radius))
            {
                RaycastHit hit = new RaycastHit();
                if (Physics.SphereCast(spawnTest, radius, Vector3.down, out hit, startHeight))
                {
                    spawnTest.y += Random.Range(0, hit.distance);
                    transform.position = spawnTest;
                    transform.rotation = Quaternion.identity;
                    return;
                }

                transform.position = spawnTest;
                transform.rotation = Quaternion.identity;
                return;
            }
        }
        Debug.LogWarning("Couldn't find a spot to place the avatar!");
    }

    public void InitNetData(NetMessenger myNewMessenger, NetMQ.Sockets.ResponseSocket myNewServer)
    {
        Debug.Log("Calling InitNetData");
        _myMessenger = myNewMessenger;
        _myServer = myNewServer;
        _request.callbackFunc = (CameraStreamer.CaptureRequest req)=>{_myMessenger.SendFrameUpdate(req, this);};
        ReadyFramesForRequest();
    }

    // Parse the input sent from the client and use it to update the controls for the next simulation segment
    public void HandleNetInput(LitJson.JsonData msgJsonData)
    {
        _myInput.HandleNetInput(msgJsonData, ref _targetVelocity);

        _readyForSimulation = true;
        // Now ready the output and run the simulation a few frames
        SimulationManager.CheckToggleUpdates();
    }
}
