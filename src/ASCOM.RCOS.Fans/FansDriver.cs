using System;
using RCOS.Tcc;
using RCOS.DriverCommon;
// using ASCOM.DeviceInterface;   // ISwitchV2
// using ASCOM.Utilities;

namespace ASCOM.RCOS
{
    /// <summary>
    /// Exposes fan mode and speed as an ASCOM ISwitchV2 device.
    /// Switch indices:
    /// 0 = Fan Auto (bool)  -> n1
    /// 1 = Fan Off  (bool)  -> n2
    /// 2 = Fan Speed (0..1) -> y{0..100}  (manual mode)
    /// 
    /// Notes:
    /// - Indices 0 and 1 are mutually exclusive; setting one clears the other.
    /// - Setting index 2 forces Manual mode (mode=0).
    /// </summary>
    public class Switch /* : ISwitchV2 */
    {
        private readonly TccClient _tcc;
        private readonly DriverProfile _profile;
        public const string DriverId = "ASCOM.RCOS.Switch";
        public string PortName { get => _profile.ComPort; set { _profile.ComPort = value; _profile.Save(); } }

        public Switch(string portName)
        {
            _profile = DriverProfile.Load(DeviceType.Switch, DriverId);
            if (portName is not null) { _profile.ComPort = portName; _profile.Save(); }
            _tcc = new TccClient(_profile.ComPort);
            _tcc.Open();
            _tcc.QueryTemperature(); // picks up :fm and :fs if device reports them in same frame
        }

        public void Dispose() => _tcc.Dispose();

        public short MaxSwitch => 3;

        public string GetSwitchName(short id) => id switch
        {
            0 => "Fan Auto",
            1 => "Fan Off",
            2 => "Fan Speed",
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };

        public string GetSwitchDescription(short id) => id switch
        {
            0 => "Set fan controller to automatic mode",
            1 => "Turn fans off",
            2 => "Manual fan speed (0..1) where 1 = 100%",
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };

        public bool CanWrite(short id) => true;

        // ----- Boolean accessors for indices 0 and 1 -----
        public bool GetSwitch(short id) => id switch
        {
            0 => _tcc.FanMode == 1,
            1 => _tcc.FanMode == 2,
            2 => throw new InvalidOperationException("Use GetSwitchValue for Fan Speed"),
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };

        public void SetSwitch(short id, bool state)
        {
            switch (id)
            {
                case 0: // Auto
                    if (state) { _tcc.SetFanMode(1); } else { _tcc.SetFanMode(0); }
                    break;
                case 1: // Off
                    if (state) { _tcc.SetFanMode(2); } else { _tcc.SetFanMode(0); }
                    break;
                case 2:
                    throw new InvalidOperationException("Use SetSwitchValue for Fan Speed");
                default:
                    throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        // ----- Analog accessor for index 2 (0..1) -----
        public double GetSwitchValue(short id)
        {
            if (id != 2) throw new InvalidOperationException("Only index 2 supports analog value.");
            return _tcc.FanSpeedPercent / 100.0;
        }

        public void SetSwitchValue(short id, double value)
        {
            if (id != 2) throw new InvalidOperationException("Only index 2 accepts analog value.");
            if (value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));

            int percent = (int)Math.Round(value * 100.0);
            _tcc.SetFanSpeedPercent(percent); // forces manual mode
        }
    }
}