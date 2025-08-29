using Microsoft.FlightSimulator.SimConnect;
using System;
using System.Runtime.InteropServices;
using WebSocketSharp;
using WebSocketSharp.Server;
using Newtonsoft.Json;

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

public struct FmaData // this is bindings for active FMA modes, mainly from the airbus, or at least i think it is
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
    private string pendingErrorMessage;
    
    public void SendData(AircraftData data)
    {
        try
        {
            if (State == WebSocketState.Open)
            {
                var settings = new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.Default };
                var json = JsonConvert.SerializeObject(data, settings);
                Send(json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PFD SendData error (non-fatal): {ex.Message}");
            // Don't rethrow - just log and continue
        }
    }
    
    public void SendError(string errorMessage)
    {
        try
        {
            if (State == WebSocketState.Open)
            {
                var errorJson = JsonConvert.SerializeObject(new { error = errorMessage, timestamp = DateTime.UtcNow });
                Send(errorJson);
            }
            else
            {
                pendingErrorMessage = errorMessage;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PFD SendError failed (non-fatal): {ex.Message}");
            // Store the error for when connection reopens
            pendingErrorMessage = errorMessage;
        }
    }

    protected override void OnOpen()
    {
        try
        {
            Console.WriteLine("PFD WebSocket connection opened");
            base.OnOpen();
            if (!string.IsNullOrEmpty(pendingErrorMessage))
            {
                SendError(pendingErrorMessage);
                pendingErrorMessage = null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PFD OnOpen error: {ex.Message}");
        }
    }

    protected override void OnError(WebSocketSharp.ErrorEventArgs e)
    {
        Console.WriteLine($"PFD WebSocket error: {e.Message}");
        // Don't call base.OnError as it might close the connection
        // Just log the error and continue
    }

    protected override void OnClose(CloseEventArgs e)
    {
        Console.WriteLine($"PFD WebSocket closed: {e.Reason} (Code: {e.Code})");
        base.OnClose(e);
    }
}

public class NDService : WebSocketBehavior
{
    private string pendingErrorMessage;
    
    public void SendData(AircraftData data)
    {
        try
        {
            if (State == WebSocketState.Open)
            {
                var settings = new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.Default };
                var json = JsonConvert.SerializeObject(data, settings);
                Send(json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ND SendData error (non-fatal): {ex.Message}");
            // Don't rethrow - just log and continue
        }
    }
    
    public void SendError(string errorMessage)
    {
        try
        {
            if (State == WebSocketState.Open)
            {
                var errorJson = JsonConvert.SerializeObject(new { error = errorMessage, timestamp = DateTime.UtcNow });
                Send(errorJson);
            }
            else
            {
                pendingErrorMessage = errorMessage;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ND SendError failed (non-fatal): {ex.Message}");
            pendingErrorMessage = errorMessage;
        }
    }

    protected override void OnOpen()
    {
        try
        {
            Console.WriteLine("ND WebSocket connection opened");
            base.OnOpen();
            if (!string.IsNullOrEmpty(pendingErrorMessage))
            {
                SendError(pendingErrorMessage);
                pendingErrorMessage = null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ND OnOpen error: {ex.Message}");
        }
    }

    protected override void OnError(WebSocketSharp.ErrorEventArgs e)
    {
        Console.WriteLine($"ND WebSocket error: {e.Message}");
        // Don't call base.OnError as it might close the connection
    }

    protected override void OnClose(CloseEventArgs e)
    {
        Console.WriteLine($"ND WebSocket closed: {e.Reason} (Code: {e.Code})");
        base.OnClose(e);
    }
}

class Program
{
    static SimConnect simconnect;
    static PFDService pfdService;
    static NDService ndService;
    static readonly object pfdServiceLock = new object();
    static readonly object ndServiceLock = new object();
    static WebSocketServer wssv;

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
        try
        {
            wssv = new WebSocketServer("ws://127.0.0.1:8080");
            
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start WebSocket server: {ex.Message}");
            Console.ReadKey();
            return;
        }

        // Connect to MSFS with retry logic
        bool simconnectConnected = false;
        int retryCount = 0;
        const int maxRetries = 5;

        while (!simconnectConnected && retryCount < maxRetries)
        {
            try
            {
                simconnect = new SimConnect("PFD Reader", IntPtr.Zero, 0, null, 0);
                Console.WriteLine("Connected to MSFS SimConnect.");
                simconnectConnected = true;

                // Register all the data points we need with SimConnect
                RegisterSimConnectData();

                simconnect.OnRecvSimobjectData += Simconnect_OnRecvSimobjectData;

                // Request data updates every frame
                simconnect.RequestDataOnSimObject(DATA_REQUESTS.Request_1, DEFINITIONS.AircraftInfo, 
                    SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SIM_FRAME, 
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (COMException ex)
            {
                retryCount++;
                Console.WriteLine($"Attempt {retryCount}: Error connecting to MSFS: {ex.Message}");
                
                if (retryCount < maxRetries)
                {
                    Console.WriteLine($"Retrying in 5 seconds...");
                    System.Threading.Thread.Sleep(5000);
                }
                else
                {
                    Console.WriteLine("Max retries reached. Continuing without SimConnect...");
                    SendErrorToAllClients("Failed to connect to MSFS after multiple attempts: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error connecting to MSFS: {ex.Message}");
                SendErrorToAllClients("Unexpected error connecting to MSFS: " + ex.Message);
                break;
            }
        }

        // Main message loop with error handling
        Console.WriteLine("Starting main loop. Press 'q' to quit.");
        
        while (true)
        {
            try
            {
                // Check for quit command
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                    {
                        break;
                    }
                }

                // Process SimConnect messages if connected
                if (simconnectConnected && simconnect != null)
                {
                    try
                    {
                        simconnect.ReceiveMessage();
                    }
                    catch (COMException ex)
                    {
                        Console.WriteLine($"SimConnect error: {ex.Message}. Attempting to reconnect...");
                        simconnectConnected = false;
                        SendErrorToAllClients("Lost connection to MSFS. Attempting to reconnect...");
                        
                        // Try to reconnect
                        TryReconnectSimConnect();
                        simconnectConnected = (simconnect != null);
                    }
                }

                System.Threading.Thread.Sleep(10); // Be a good citizen
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Main loop error: {ex.Message}");
                // Continue running even if there's an error
            }
        }

        // Cleanup
        Console.WriteLine("Shutting down...");
        try
        {
            simconnect?.Dispose();
            wssv?.Stop();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cleanup error: {ex.Message}");
        }
    }

    private static void RegisterSimConnectData()
    {
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
    }

    private static void TryReconnectSimConnect()
    {
        try
        {
            simconnect?.Dispose();
            simconnect = null;
            
            System.Threading.Thread.Sleep(2000); // Wait before reconnecting
            
            simconnect = new SimConnect("PFD Reader", IntPtr.Zero, 0, null, 0);
            RegisterSimConnectData();
            simconnect.OnRecvSimobjectData += Simconnect_OnRecvSimobjectData;
            simconnect.RequestDataOnSimObject(DATA_REQUESTS.Request_1, DEFINITIONS.AircraftInfo, 
                SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SIM_FRAME, 
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
                
            Console.WriteLine("Reconnected to MSFS SimConnect.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Reconnection failed: {ex.Message}");
            simconnect = null;
        }
    }

    private static void SendErrorToAllClients(string errorMessage)
    {
        lock (pfdServiceLock)
        {
            pfdService?.SendError(errorMessage);
        }
        lock (ndServiceLock)
        {
            ndService?.SendError(errorMessage);
        }
    }

    private static void Simconnect_OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA e)
    {
        try
        {
            if (e.dwRequestID == (uint)DATA_REQUESTS.Request_1)
            {
                var data = (AircraftData)e.dwData[0];
                
                lock (pfdServiceLock)
                {
                    pfdService?.SendData(data);
                }
                lock (ndServiceLock)
                {
                    ndService?.SendData(data);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Data processing error: {ex.Message}");
            // Don't rethrow - just log and continue
        }
    }
}