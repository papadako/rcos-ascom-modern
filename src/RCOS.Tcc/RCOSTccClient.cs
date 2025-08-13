using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RCOS.Tcc
{
    /// <summary>
    /// Modern, thread-safe C# client for the RCOS TCC serial protocol.
    /// Derived by reading the legacy VB6 classes (Serial.cls, RCOS.cls, Rotator.cls, Temperature.cls).
    /// 
    /// This client focuses on: Focuser, Rotator, and Temperature/Fan control.
    /// It exposes simple async methods and properties and maintains a background
    /// parser that tokenizes the TCC stream (space-delimited, colon-prefixed keys).
    /// </summary>
    public sealed class TccClient : IDisposable
    {
        private readonly SerialPort _port;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _readerTask;
        private readonly StringBuilder _rxBuf = new();
        private readonly object _sendLock = new();

        // ---------------- State (updated by parser) ----------------
        // Focuser
        public int FocuserSetPos { get; private set; }
        public int FocuserActPos { get; private set; }
        public bool FocuserIsMoving { get; private set; }
        public bool FocuserHomed { get; private set; }

        // Rotator (units: degrees, device reports steps where 200 steps == 1 deg)
        public double RotatorSetPosDeg { get; private set; }   // lclSetPos / 200.0
        public double RotatorActPosDeg { get; private set; }   // lclActPos / 200.0
        public bool RotatorIsMoving { get; private set; }
        public bool RotatorHomed { get; private set; }

        // Temperature (deg F as reported; convert externally if you wish)
        public double AmbientTempF { get; private set; }
        public double PrimaryTempF { get; private set; }
        public double SecondaryTempF { get; private set; }
        public double ElectronicsTempF { get; private set; }

        // Fan
        // 0=manual,1=auto,2=off (per Temperature.cls FanMode)
        public int FanMode { get; private set; }
        // 0..100 (per Temperature.cls FanSpeed)
        public int FanSpeedPercent { get; private set; }

        // Secondary heater (dew on secondary)
        // Mode: 0=manual, 1=auto, 2=off
        public int SecondaryHeaterMode { get; private set; }
        // 0..100 manual power
        public int SecondaryHeaterPowerPercent { get; private set; }
        // Setpoint in deg C offset (-10..+10)
        public double SecondaryHeaterSetpointC { get; private set; }

        // Separate dew heater channels (e.g., for primary/ancillary)
        public int Dew1PowerPercent { get; private set; }
        public int Dew2PowerPercent { get; private set; }

        // Fan control PID-like params
        // Gain (1.0..10.0), Deadband (0.0..10.0)
        public double FanGain { get; private set; }
        public double FanDeadband { get; private set; }

        // Ping / firmware
        public bool LastPingOk { get; private set; }
        public string FirmwareVersion { get; private set; } = string.Empty;

        // Raw tokens queue (optional external inspection)
        public ConcurrentQueue<(string key, string value, DateTime ts)> RawTokens { get; } = new();

        public bool IsOpen => _port.IsOpen;

        public TccClient(string portName, int baudRate = 9600)
        {
            _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.XOnXOff, // matches VB: m_COM.Handshaking = 1 (XOnXOff? RTS/CTS? depends on MSCOMM; RTS/CTS=2; XOnXOff=1)
                DtrEnable = false,
                RtsEnable = false,
                ReadTimeout = 100,
                WriteTimeout = 1000,
                NewLine = " " // stream uses space as delimiter
            };

            // Open lazily; you may call Open() explicitly
            _readerTask = Task.CompletedTask;
        }

        public void Open()
        {
            if (_port.IsOpen) return;
            _port.Open();
            // Start background reader
            var task = Task.Run(ReadLoop, _cts.Token);
            Volatile.Write(ref UnsafeReaderTaskRef, task);
        }

        public void Close()
        {
            _cts.Cancel();
            try { UnsafeReaderTaskRef?.Wait(1000); } catch { /* ignore */ }
            if (_port.IsOpen) _port.Close();
        }

        private Task? UnsafeReaderTaskRef;

        private async Task ReadLoop()
        {
            var buf = new byte[4096];
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    int n = await _port.BaseStream.ReadAsync(buf.AsMemory(0, buf.Length), _cts.Token);
                    if (n > 0) ProcessIncoming(Encoding.ASCII.GetString(buf, 0, n));
                    else await Task.Delay(5, _cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (TimeoutException) { /* ignore */ }
                catch
                {
                    // prevent tight loop on unexpected errors
                    await Task.Delay(50);
                }
            }
        }

        private void ProcessIncoming(string chunk)
        {
            lock (_rxBuf)
            {
                _rxBuf.Append(chunk);
                // Tokenization: protocol is space-delimited. Replies are colon-prefixed keys (":s", ":a", ":t1", etc.) followed by a value.
                while (true)
                {
                    int spaceIdx = _rxBuf.ToString().IndexOf(' ');
                    if (spaceIdx < 0) break;
                    var tok = _rxBuf.ToString(0, spaceIdx);
                    _rxBuf.Remove(0, spaceIdx + 1);
                    if (tok.Length == 0) continue;

                    // If this token is a key (starts with ':'), the next token should be its value.
                    if (tok.StartsWith(":"))
                    {
                        // Pull the next token as value (may already be available; if not, stash and wait)
                        int nextSpace = _rxBuf.ToString().IndexOf(' ');
                        if (nextSpace < 0) { _rxBuf.Insert(0, tok + " "); break; } // put back and wait for more
                        string val = _rxBuf.ToString(0, nextSpace);
                        _rxBuf.Remove(0, nextSpace + 1);
                        DispatchKeyValue(tok, val);
                    }
                    else
                    {
                        // Occasionally we may see a bare value or bang confirmation. Treat ":!" specially like a no-value ping.
                        if (tok == ":!") { LastPingOk = true; RawTokens.Enqueue((tok, "", DateTime.UtcNow)); }
                        // else ignore or enqueue raw
                    }
                }
            }
        }

        private static int SafeParseInt(string s) => int.TryParse(s, out var v) ? v : 0;
        private static double SafeParseDouble(string s) => double.TryParse(s, out var v) ? v : double.NaN;

        private void DispatchKeyValue(string key, string value)
        {
            RawTokens.Enqueue((key, value, DateTime.UtcNow));

            switch (key)
            {
                // ---------------- Focuser status (RCOS.cls) ----------------
                case ":s": FocuserSetPos = SafeParseInt(value); break; // Set position (steps)
                case ":a": // Actual position (steps) and movement inference
                    {
                        var act = SafeParseInt(value);
                        FocuserActPos = act;
                        FocuserIsMoving = Math.Abs(FocuserSetPos - FocuserActPos) > 5;
                        break;
                    }
                case ":h": FocuserHomed = value == "1"; break;
                case ":ims": /* ideal mirror spacing */ break;
                case ":tc": // temp compensation state: 0=off, 1=auto, 2=manual
                    // Nothing to store here directly; users can query via commands.
                    break;

                // ---------------- Temperature (Temperature.cls) ----------------
                // Temps are reported as integer hundredths (value/100)
                case ":t1": AmbientTempF = SafeParseInt(value) / 100.0; break;
                case ":t2": PrimaryTempF = SafeParseInt(value) / 100.0; break;
                case ":t3": SecondaryTempF = SafeParseInt(value) / 100.0; break;
                case ":t7": ElectronicsTempF = SafeParseInt(value) / 100.0; break;

                case ":t4": /* ambient raw */ break;
                case ":t5": /* primary raw */ break;
                case ":t6": /* secondary raw */ break;

                // Fan mode/speed and tuning
                case ":fm": FanMode = SafeParseInt(value); break;      // 0=manual,1=auto,2=off
                case ":fs": FanSpeedPercent = SafeParseInt(value); break;
                case ":fg": FanGain = SafeParseInt(value) / 10.0; break;    // gain x10
                case ":ft": FanDeadband = SafeParseInt(value) / 10.0; break; // deadband x10

                // Secondary heater (dew around secondary mirror)
                case ":sm": SecondaryHeaterMode = SafeParseInt(value); break; // 0=manual,1=auto,2=off
                case ":ss": SecondaryHeaterPowerPercent = Math.Min(100, Math.Max(0, SafeParseInt(value))); break;
                case ":st": SecondaryHeaterSetpointC = SafeParseInt(value) / 10.0; break; // signed tenths

                // Dedicated dew channels
                case ":d1": Dew1PowerPercent = Math.Min(100, Math.Max(0, SafeParseInt(value))); break;
                case ":d2": Dew2PowerPercent = Math.Min(100, Math.Max(0, SafeParseInt(value))); break;

                // Fan mode/speed
                case ":fm": FanMode = SafeParseInt(value); break;      // 0=manual,1=auto,2=off
                case ":fs": FanSpeedPercent = SafeParseInt(value); break;

                // Firmware
                case ":vr": FirmwareVersion = value; break;

                // ---------------- Rotator (Rotator.cls) ----------------
                case ":rs": RotatorSetPosDeg = SafeParseInt(value) / 200.0; break;
                case ":rt":
                    {
                        var act = SafeParseInt(value) / 200.0;
                        RotatorActPosDeg = act;
                        RotatorIsMoving = Math.Abs(RotatorSetPosDeg - RotatorActPosDeg) > 0;
                        break;
                    }
                case ":rh": RotatorHomed = value == "1"; break;

                default:
                    // Unknown, leave in queue for diagnostics
                    break;
            }
        }

        // ---------------- Command helpers ----------------

        private void Send(string text)
        {
            lock (_sendLock)
            {
                if (!_port.IsOpen) throw new InvalidOperationException("Serial port is not open.");
                _port.Write(text);
            }
        }

        public void Dispose()
        {
            Close();
            _cts.Dispose();
            _port.Dispose();
        }

        // High-level ops

        public void Ping()
        {
            LastPingOk = false;
            Send("! ");
            // Let background parser set LastPingOk on ":!"
        }

        // ----- Focuser -----
        public void QueryFocuser() => Send(" Q ");

        public void MoveFocuserAbsolute(int positionSteps)
        {
            // Legacy code writes commands elsewhere; we provide a simple absolute move via delta calculation.
            // If your firmware supports an absolute command, replace with that text.
            int delta = positionSteps - FocuserActPos;
            MoveFocuserRelative(delta);
        }

        public void MoveFocuserRelative(int deltaSteps)
        {
            // Using "+" or "-" commands depends on firmware; not observed explicitly in the VB6 snippets.
            // Many TCC firmwares accept " m{delta}" or similar; if not, adapt here.
            // Placeholder relative move encoding:
            string sign = deltaSteps >= 0 ? "+" : "-";
            Send($" m{sign}{Math.Abs(deltaSteps)} ");
            // After commanding, ask for status soon after
            Send(" Q ");
        }

        public void StopFocuser()
        {
            Send(" s "); // If your firmware uses another stop code, change this.
        }

        public void HomeFocuser()
        {
            Send(" h ");
        }

        public void SetTempComp(bool enabled, bool manualMode)
        {
            if (!enabled) Send(" +0 ");
            else if (manualMode) Send(" +2 ");
            else Send(" +1 ");
        }

        public void QueryTemperature() => Send(" T ");

        
        // ----- Dew / Secondary heater / Fan tuning -----
        public void SetSecondaryHeaterMode(int mode)
        {
            // 0=manual, 1=auto, 2=off
            switch (mode)
            {
                case 0: // manual retains current power
                    SecondaryHeaterMode = 0;
                    // Ensure we send the current power to confirm manual
                    Send($"s{Math.Min(100, Math.Max(0, SecondaryHeaterPowerPercent))} ");
                    break;
                case 1: Send("w1 "); SecondaryHeaterMode = 1; break;
                case 2: Send("w2 "); SecondaryHeaterMode = 2; SecondaryHeaterPowerPercent = 0; break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        public void SetSecondaryHeaterPowerPercent(int percent)
        {
            if (percent < 0 || percent > 100) throw new ArgumentOutOfRangeException(nameof(percent));
            Send($"s{percent} ");
            SecondaryHeaterPowerPercent = percent;
            SecondaryHeaterMode = 0; // manual
        }

        public void SetSecondaryHeaterSetpointC(double setpointC)
        {
            if (setpointC < -10.0 || setpointC > 10.0) throw new ArgumentOutOfRangeException(nameof(setpointC));
            int tenths = (int)Math.Round(setpointC * 10.0);
            Send($"P{tenths} ");
            SecondaryHeaterSetpointC = setpointC;
        }

        public void SetDew1PowerPercent(int percent)
        {
            if (percent < 0 || percent > 100) throw new ArgumentOutOfRangeException(nameof(percent));
            Send($"c{percent} ");
            Dew1PowerPercent = percent;
        }

        public void SetDew2PowerPercent(int percent)
        {
            if (percent < 0 || percent > 100) throw new ArgumentOutOfRangeException(nameof(percent));
            Send($"k{percent} ");
            Dew2PowerPercent = percent;
        }

        public void SetFanGain(double gain)
        {
            if (gain < 0.1 || gain > 10.0) throw new ArgumentOutOfRangeException(nameof(gain));
            int x10 = (int)Math.Round(gain * 10.0);
            Send($" g{x10} ");
            FanGain = gain;
        }

        public void SetFanDeadband(double deadband)
        {
            if (deadband < 0.0 || deadband > 10.0) throw new ArgumentOutOfRangeException(nameof(deadband));
            int x10 = (int)Math.Round(deadband * 10.0);
            Send($" O{x10} ");
            FanDeadband = deadband;
        }
// ----- Rotator -----
        public void QueryRotator() => Send(" R ");

        public void HomeRotator() => Send(" r ");

        public void MoveRotatorAbsoluteDeg(double angleDeg)
        {
            // Convert deg -> device steps (200 steps per degree)
            int targetSteps = (int)Math.Round(angleDeg * 200.0);
            // Protocol in VB6 uses "M" and "m" variants; here we provide a relative move placeholder:
            int currentSteps = (int)Math.Round(RotatorActPosDeg * 200.0);
            int delta = targetSteps - currentSteps;
            MoveRotatorRelativeDeg(delta / 200.0);
        }

        public void MoveRotatorRelativeDeg(double deltaDeg)
        {
            int deltaSteps = (int)Math.Round(deltaDeg * 200.0);
            string sign = deltaSteps >= 0 ? "+" : "-";
            Send($" r{sign}{Math.Abs(deltaSteps)} ");
            Send(" R ");
        }

        // ----- Fan control -----
        public void SetFanMode(int mode)
        {
            // 0=manual => we keep current speed
            // 1=auto   => "n1 "
            // 2=off    => "n2 "
            switch (mode)
            {
                case 0:
                    // manual uses "y{speed}" to set actual speed; just set mode var
                    FanMode = 0;
                    break;
                case 1:
                    Send("n1 ");
                    FanMode = 1;
                    break;
                case 2:
                    Send("n2 ");
                    FanMode = 2;
                    FanSpeedPercent = 0;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), "Fan mode must be 0, 1 or 2.");
            }
        }

        public void SetFanSpeedPercent(int speed0to100)
        {
            if (speed0to100 < 0 || speed0to100 > 100) throw new ArgumentOutOfRangeException(nameof(speed0to100));
            Send($"y{speed0to100} ");
            FanSpeedPercent = speed0to100;
            FanMode = 0; // manual
        }
    }
}