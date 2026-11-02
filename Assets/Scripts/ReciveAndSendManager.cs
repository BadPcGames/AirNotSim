using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class ReciveAndSendManager : MonoBehaviour
{
    public EngineController EngineController;
    public DronePhysics Physics;

    [Header("Network Settings")]
    public string externalAppIP = "127.0.0.1";
    public int telemetryPort = 9001;
    public int motorPort = 9002;

    private UdpClient udpTelemetrySender;
    private UdpClient udpMotorReceiver;
    private Thread receiveThread;

    private float m1, m2, m3, m4;
    private bool isRunning = true;

    void Start()
    {
        udpTelemetrySender = new UdpClient();
        udpMotorReceiver = new UdpClient(motorPort);

        receiveThread = new Thread(ReceiveMotorCommands);
        receiveThread.IsBackground = true;
        receiveThread.Start();
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

    void SendTelemetry()
    {
        if (Physics == null) return;
        Transform t = Physics.gameObject.transform;

        TelemetryData data = new TelemetryData
        {
            pitch = t.eulerAngles.x,
            yaw = t.eulerAngles.y,
            roll = t.eulerAngles.z,
            gpsLat = 50.45f + (t.position.z * 0.00001f),
            gpsLon = 30.52f + (t.position.x * 0.00001f),
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
            catch (System.Exception ex)
            {
                Debug.LogWarning("Motor receive error (or thread stop): " + ex.Message);
            }
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        udpTelemetrySender?.Close();
        udpMotorReceiver?.Close();
        receiveThread?.Abort();
    }
}

[System.Serializable]
public class TelemetryData { public float pitch, roll, yaw, gpsLat, gpsLon, gpsAlt; }

[System.Serializable]
public class MotorCommands { public float m1, m2, m3, m4; }