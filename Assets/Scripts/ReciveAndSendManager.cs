using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

public class ReciveAndSendManager : MonoBehaviour
{
    public EngineController EngineController;
    public DronePhysics Physics;

    public List<Anten> antens;

    [Header("Network Settings")]
    public string externalAppIP = "127.0.0.1";
    public int telemetryPort = 9001;
    public int motorPort = 9002;
    public int videoPort = 9003;

    [Header("Video Settings")]
    public RenderTexture droneCameraRT;
    [Range(5, 60)] public int videoFps = 30;
    [Range(10, 100)] public int jpegQuality = 50;
    [Range(5, 60)] public int maxVideoFps = 30;
    [Range(10, 100)] public int maxJpegQuality = 50;

    [Header("Signal Testing (Debug)")]
    [Tooltip("Если включено, берет сигнал из ползунка ниже. Если выключено - считает физику.")]
    public bool overrideSignal = true;
    [Range(0, 100)] public float currentSignal = 100f;

    private UdpClient udpTelemetrySender;
    private UdpClient udpMotorReceiver;
    private UdpClient udpVideoSender;
    private Thread receiveThread;

    private float m1, m2, m3, m4;
    private bool isRunning = true;

    private Texture2D videoTextureCopy;
    private float lastVideoFrameTime;
    private bool isFrameProcessing = false;

    private void Start()
    {
        udpTelemetrySender = new UdpClient();
        udpMotorReceiver = new UdpClient(motorPort);
        udpVideoSender = new UdpClient();

        receiveThread = new Thread(ReceiveMotorCommands);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        if (droneCameraRT == null)
            Debug.LogError("[VIDEO ERROR] Не назначен droneCameraRT!");
    }

    private void Update()
    {
        ProcessVideoStreaming();
    }

    private void FixedUpdate()
    {
        SendTelemetry();

        if (EngineController != null && EngineController.throtle.Length >= 4)
        {
            EngineController.throtle[0] = m1;
            EngineController.throtle[1] = m2;
            EngineController.throtle[2] = m3;
            EngineController.throtle[3] = m4;
        }
    }

    private void ProcessVideoStreaming()
    {
        if (droneCameraRT == null || isFrameProcessing) return;

        if (Time.time - lastVideoFrameTime >= (1.0f / videoFps))
        {
            lastVideoFrameTime = Time.time;
            isFrameProcessing = true;
            AsyncGPUReadback.Request(droneCameraRT, 0, OnGPUReadbackCompleted);
        }
    }

    private void OnGPUReadbackCompleted(AsyncGPUReadbackRequest request)
    {
        isFrameProcessing = false;
        if (!isRunning || request.hasError) return;

        if (videoTextureCopy == null)
            videoTextureCopy = new Texture2D(droneCameraRT.width, droneCameraRT.height, TextureFormat.RGBA32, false);

        videoTextureCopy.LoadRawTextureData(request.GetData<byte>());
        videoTextureCopy.Apply(false, false);
        byte[] jpegBytes = videoTextureCopy.EncodeToJPG(jpegQuality);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { udpVideoSender?.Send(jpegBytes, jpegBytes.Length, externalAppIP, videoPort); }
            catch { }
        });
    }

    private void SendTelemetry()
    {
        if (Physics == null) return;
        Transform t = Physics.gameObject.transform;
        float finalSignal = overrideSignal ? currentSignal : CalculateSignalLevel();

        if (!overrideSignal) currentSignal = finalSignal;

        TelemetryData data = new TelemetryData
        {
            pitch = t.eulerAngles.x,
            yaw = t.eulerAngles.y,
            roll = t.eulerAngles.z,
            gpsLat = 50.45d + ((double)t.position.z * 0.00001d),
            gpsLon = 30.52d + ((double)t.position.x * 0.00001d),
            gpsAlt = t.position.y,
            signalPower = finalSignal 
        };

        string json = JsonUtility.ToJson(data);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        udpTelemetrySender.Send(bytes, bytes.Length, externalAppIP, telemetryPort);
    }

    private void ReceiveMotorCommands()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, motorPort);
        while (isRunning)
        {
            try
            {
                byte[] bytes = udpMotorReceiver.Receive(ref remoteEndPoint);
                string json = Encoding.UTF8.GetString(bytes);
                MotorCommands cmds = JsonUtility.FromJson<MotorCommands>(json);
                m1 = cmds.m1; m2 = cmds.m2; m3 = cmds.m3; m4 = cmds.m4;
            }
            catch { }
        }
    }

    private void OnApplicationQuit()
    {
        isRunning = false;
        udpTelemetrySender?.Close();
        udpMotorReceiver?.Close();
        udpVideoSender?.Close();
        if (videoTextureCopy != null) Destroy(videoTextureCopy);
    }

    private float CalculateSignalLevel()
    {
        if (Physics == null || antens == null || antens.Count == 0) return 0f;

        float bestSignal = 0f;
        Vector3 dronePos = Physics.gameObject.transform.position;

        for (int i = 0; i < antens.Count; i++)
        {
            Anten anten = antens[i];
            if (anten == null) continue;

            Vector3 directionToAntenna = anten.transform.position - dronePos;
            float sqrDistance = directionToAntenna.sqrMagnitude;
            float maxRSqr = anten.maxRange * anten.maxRange;

            if (sqrDistance > maxRSqr) continue;

            float distance = Mathf.Sqrt(sqrDistance);
            float normalizedDistance = distance / anten.maxRange;
            float rawSignal = anten.signalFalloff.Evaluate(normalizedDistance) * 100f;

            Vector3 rayStartPos = dronePos + directionToAntenna.normalized * 1.5f;
            if (UnityEngine.Physics.Raycast(rayStartPos, directionToAntenna.normalized, distance - 1.5f, anten.obstacleMask))
            {
                rawSignal *= anten.obstaclePenalty;
            }

            if (rawSignal > bestSignal) bestSignal = rawSignal;
        }
        return Mathf.Clamp(bestSignal, 0f, 100f);
    }
}

[System.Serializable]
public class TelemetryData
{
    public float pitch, roll, yaw, gpsAlt;
    public double gpsLat, gpsLon;
    public float signalPower;
}

[System.Serializable]
public class MotorCommands { public float m1, m2, m3, m4; }