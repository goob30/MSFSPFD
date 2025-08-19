using Microsoft.FlightSimulator.SimConnect;
using System;
using System.Runtime.InteropServices;
using WebSocketSharp;
using WebSocketSharp.Server;
using Newtonsoft.Json; // You may need to add this NuGet package: Install-Package Newtonsoft.Json



/*
    Half of this entire project is vibe coded because i wrote most of it at past 3 in the morning
    and i don't know how half of this works, but i wont touch it because if it aint broke dont fix
    it
*/

// Define enums for SimConnect requests
enum DEFINITIONS { AircraftInfo }
enum DATA_REQUESTS { Request_1 }

// This struct now includes all the data needed for the advanced PFD
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
public struct AircraftData
{
    public double pitch { get; set; }
    public double bank { get; set; }
    public double airspeed { get; set; }
    public double altitude { get; set; }
    public double heading { get; set; }
    public double verticalSpeed { get; set; }
    public double selectedAltitude { get; set; }
    public double selectedAirspeed { get; set; }
    public double qnh { get; set; }

    public FmaData fma { get; set; }
}

public struct FmaData
{
    // Col 1
    public bool autoPilotMaster { get; set; }
    public bool autopilot2Active { get; set; }
    public bool flightDirectorActive { get; set; }
    public bool autothrottleActive { get; set; }
    public double speedSlotIndex { get; set; }
    public bool managedSpeedInMach { get; set; }
    public bool aFloorActive { get; set; }

    // Col 2
    public bool headingLock { get; set; }
    public bool navLock { get; set; }
    public bool wingLevelerActive { get; set; }
    public bool rwyTrackActive { get; set; }
    public bool locLock { get; set; }

    // Col 3
    public bool altitudeLock;
    public bool verticalSpeedHold;
    public bool glideslopeActive;
    public bool glideslopeArmed;
    public bool altitudeArmed;


}

public class PFDService : WebSocketBehavior
{
    // This method now serializes the entire AircraftData struct to JSON
    private string pendingErrorMessage;
    public void SendData(AircraftData data)
    {
        if (State == WebSocketState.Open)
        {
            // Use Newtonsoft.Json for reliable serialization
            var settings = new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.Default };
            var json = JsonConvert.SerializeObject(data, settings);
            Send(json);
        }
    }
    public void SendError(string errorMessage)
    {
        if (State == WebSocketState.Open)
        {
            var errorJson = JsonConvert.SerializeObject(new { error = errorMessage });
            Send(errorJson);
        }

        else
        {
            pendingErrorMessage = errorMessage;
        }
    }

    protected override void OnOpen()
    {
        base.OnOpen();
        if (!string.IsNullOrEmpty(pendingErrorMessage))
        {
            SendError(pendingErrorMessage);
            pendingErrorMessage = null;
        }
    }

}

public class NDService : WebSocketBehavior
{
    private string pendingErrorMessage;
    public void SendData(AircraftData data)
    {
        if (State == WebSocketState.Open)
        {
            // Use Newtonsoft.Json for reliable serialization
            var settings = new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.Default };
            var json = JsonConvert.SerializeObject(data, settings);
            Send(json);
        }
    }
    public void SendError(string errorMessage)
    {
        if (State == WebSocketState.Open)
        {
            var errorJson = JsonConvert.SerializeObject(new { error = errorMessage });
            Send(errorJson);
        }

        else
        {
            pendingErrorMessage = errorMessage;
        }
    }

    protected override void OnOpen()
    {
        base.OnOpen();
        if (!string.IsNullOrEmpty(pendingErrorMessage))
        {
            SendError(pendingErrorMessage);
            pendingErrorMessage = null;
        }
    }
}

class Program
{
    static SimConnect simconnect;
    static PFDService pfdService;
    static NDService ndService;
    static readonly object pfdServiceLock = new object();
    static readonly object ndServiceLock = new object();

    /// <summary>
    /// Entry point of the application. 
    /// Initializes and starts a WebSocket server for PFD data streaming, 
    /// connects to Microsoft Flight Simulator (MSFS) using SimConnect, 
    /// registers required aircraft data definitions, and continuously receives 
    /// and processes simulation data. Handles connection errors and ensures 
    /// proper cleanup of resources on exit.
    /// </summary>
    static void Main()
    {
        // Start WebSocket server
        var wssv = new WebSocketServer("ws://127.0.0.1:8080");
        wssv.AddWebSocketService<PFDService>("/pfd", () =>
        {
            lock (pfdServiceLock)
            {
                if (pfdService == null)
                {
                    pfdService = new PFDService();
                }
            }
            return pfdService;
        });
        wssv.AddWebSocketService<NDService>("/nd", () =>
        {
            lock (ndServiceLock)
            {
                if (ndService == null)
                {
                    ndService = new NDService();
                }
            }
            return ndService;
        });
        wssv.Start();
        Console.WriteLine("WebSocket server started on ws://127.0.0.1:8080");
        Console.WriteLine("PFD available at /pfd");
        Console.WriteLine("ND available at /nd");

        // Connect to MSFS
        try
        {
            simconnect = new SimConnect("PFD Reader", IntPtr.Zero, 0, null, 0);
            Console.WriteLine("Connected to MSFS SimConnect.");

            // Register all the data points we need with SimConnect
            simconnect.AddToDataDefinition(DEFINITIONS.AircraftInfo, "PLANE PITCH DEGREES", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            simconnect.AddToDataDefinition(DEFINITIONS.AircraftInfo, "PLANE BANK DEGREES", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            simconnect.AddToDataDefinition(DEFINITIONS.AircraftInfo, "AIRSPEED INDICATED", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            simconnect.AddToDataDefinition(DEFINITIONS.AircraftInfo, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            simconnect.AddToDataDefinition(DEFINITIONS.AircraftInfo, "PLANE HEADING DEGREES MAGNETIC", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            simconnect.AddToDataDefinition(DEFINITIONS.AircraftInfo, "VERTICAL SPEED", "feet per minute", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            simconnect.AddToDataDefinition(DEFINITIONS.AircraftInfo, "AUTOPILOT ALTITUDE LOCK VAR", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            simconnect.AddToDataDefinition(DEFINITIONS.AircraftInfo, "AUTOPILOT AIRSPEED HOLD VAR", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            simconnect.AddToDataDefinition(DEFINITIONS.AircraftInfo, "AUTOPILOT MANAGED SPEED IN MACH", "mach", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            simconnect.AddToDataDefinition(DEFINITIONS.AircraftInfo, "KOHLSMAN SETTING MB", "millibars", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);


            simconnect.RegisterDataDefineStruct<AircraftData>(DEFINITIONS.AircraftInfo);

            simconnect.OnRecvSimobjectData += Simconnect_OnRecvSimobjectData;

            // Request data updates every frame
            simconnect.RequestDataOnSimObject(DATA_REQUESTS.Request_1, DEFINITIONS.AircraftInfo, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            // Main message loop
            while (true)
            {
                simconnect.ReceiveMessage();
                System.Threading.Thread.Sleep(10); // Be a good citizen
            }
        }
        catch (COMException ex)
        {
            Console.WriteLine($"Error connecting to MSFS: {ex.Message}");
            lock (pfdServiceLock)
            {
                if (pfdService != null)
                {
                    pfdService.SendError("Error connecting to MSFS: " + ex.Message);
                }
            }
        }
        finally
        {
            simconnect?.Dispose();
            wssv.Stop();
        }
    }

    private static void Simconnect_OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA e)
    {
        if (e.dwRequestID == (uint)DATA_REQUESTS.Request_1)
        {
            var data = (AircraftData)e.dwData[0];
            lock (pfdServiceLock)
            {
                pfdService?.SendData(data);
            }
        }
    }
}