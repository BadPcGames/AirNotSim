using System;
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

    [Header("Network Settings")]
    public string externalAppIP = "127.0.0.1";
    public int telemetryPort = 9001;
    public int motorPort = 9002;
    public int videoPort = 9003;

    [Header("Video Settings")]
    public RenderTexture droneCameraRT; 
    [Range(5, 60)] public int videoFps = 30;
    [Range(10, 100)] public int jpegQuality = 50;

    private UdpClient udpTelemetrySender;
    private UdpClient udpMotorReceiver;
    private UdpClient udpVideoSender;
    private Thread receiveThread;

    private float m1, m2, m3, m4;
    private bool isRunning = true;

    private Texture2D videoTextureCopy;
    private float lastVideoFrameTime;
    private bool isFrameProcessing = false;

    void Start()
    {
        udpTelemetrySender = new UdpClient();
        udpMotorReceiver = new UdpClient(motorPort);
        udpVideoSender = new UdpClient();

        receiveThread = new Thread(ReceiveMotorCommands);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        if (droneCameraRT == null)
        {
            Debug.LogError("[VIDEO ERROR] Не назначен droneCameraRT в инспекторе ReciveAndSendManager!");
        }
    }

    void Update()
    {
        ProcessVideoStreaming();
    }

    void FixedUpdate()
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

        if (!isRunning) return;

        if (request.hasError)
        {
            Debug.LogWarning("[VIDEO ERROR] Ошибка чтения из GPU (AsyncGPUReadback error)!");
            return;
        }

        if (videoTextureCopy == null)
        {
            videoTextureCopy = new Texture2D(droneCameraRT.width, droneCameraRT.height, TextureFormat.RGBA32, false);
        }

        videoTextureCopy.LoadRawTextureData(request.GetData<byte>());
        videoTextureCopy.Apply(false, false);
        byte[] jpegBytes = videoTextureCopy.EncodeToJPG(jpegQuality);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                udpVideoSender?.Send(jpegBytes, jpegBytes.Length, externalAppIP, videoPort);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Ошибка отправки видео: " + ex.Message);
            }
        });
    }

    void SendTelemetry()
    {
        if (Physics == null) return;
        Transform t = Physics.gameObject.transform;

        TelemetryData data = new TelemetryData
        {
            pitch = t.eulerAngles.x,
            yaw = t.eulerAngles.y,
            roll = t.eulerAngles.z,
            gpsLat = 50.45d + ((double)t.position.z * 0.00001d),
            gpsLon = 30.52d + ((double)t.position.x * 0.00001d),
            gpsAlt = t.position.y
        };

        string json = JsonUtility.ToJson(data);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        udpTelemetrySender.Send(bytes, bytes.Length, externalAppIP, telemetryPort);
    }

    void ReceiveMotorCommands()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, motorPort);
        while (isRunning)
        {
            try
            {
                byte[] bytes = udpMotorReceiver.Receive(ref remoteEndPoint);
                string json = Encoding.UTF8.GetString(bytes);
                MotorCommands cmds = JsonUtility.FromJson<MotorCommands>(json);

                m1 = cmds.m1;
                m2 = cmds.m2;
                m3 = cmds.m3;
                m4 = cmds.m4;
            }
            catch (Exception ex)
            {
                if (isRunning)
                    Debug.LogWarning("Motor receive error: " + ex.Message);
            }
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        udpTelemetrySender?.Close();
        udpMotorReceiver?.Close();
        udpVideoSender?.Close();

        if (videoTextureCopy != null)
            Destroy(videoTextureCopy);

        receiveThread?.Abort();
    }
}

[System.Serializable]
public class TelemetryData { 
    public float pitch, roll, yaw, gpsAlt;
    public double gpsLat, gpsLon;
}

[System.Serializable]
public class MotorCommands { public float m1, m2, m3, m4; }