using System;
using RCOS.Tcc;
using RCOS.DriverCommon;
// using ASCOM.DeviceInterface;   // ISwitchV2
// using ASCOM.Utilities;

namespace ASCOM.RCOS
{
    /// <summary>
    /// RCOS Dew / Secondary Heater as ISwitchV2
    /// Indices:
    /// 0 - Secondary Heater Auto (bool)      -> w1
    /// 1 - Secondary Heater Off  (bool)      -> w2
    /// 2 - Secondary Manual Power (0..1)     -> s{0..100}, sets mode=manual
    /// 3 - Secondary Setpoint  (-10..+10 C)  -> P{tenths}, mapped from 0..1
    /// 4 - Dew1 Power (0..1)                 -> c{0..100}
    /// 5 - Dew2 Power (0..1)                 -> k{0..100}
    /// </summary>
    public class Dew /* : ISwitchV2 */
    {
        private readonly TccClient _tcc;
        private readonly DriverProfile _profile;
        public const string DriverId = "ASCOM.RCOS.Dew";
        public string PortName { get => _profile.ComPort; set { _profile.ComPort = value; _profile.Save(); } }

        public Dew(string portName)
        {
            _profile = DriverProfile.Load(DeviceType.Dew, DriverId);
            if (portName is not null) { _profile.ComPort = portName; _profile.Save(); }
            _tcc = new TccClient(_profile.ComPort);
            _tcc.Open();
            _tcc.QueryTemperature(); // picks up :sm/:ss/:st/:d1/:d2 if present
        }

        public void Dispose() => _tcc.Dispose();

        public short MaxSwitch => 6;

        public string GetSwitchName(short id) => id switch
        {
            0 => "Secondary Auto",
            1 => "Secondary Off",
            2 => "Secondary Manual Power",
            3 => "Secondary Setpoint (-10..+10 C)",
            4 => "Dew1 Power",
            5 => "Dew2 Power",
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };

        public string GetSwitchDescription(short id) => GetSwitchName(id);

        public bool CanWrite(short id) => true;

        // Boolean indices
        public bool GetSwitch(short id) => id switch
        {
            0 => _tcc.SecondaryHeaterMode == 1,
            1 => _tcc.SecondaryHeaterMode == 2,
            _ => throw new InvalidOperationException("Use GetSwitchValue for analog indices."),
        };

        public void SetSwitch(short id, bool state)
        {
            switch (id)
            {
                case 0: // Auto
                    _tcc.SetSecondaryHeaterMode(state ? 1 : 0);
                    break;
                case 1: // Off
                    _tcc.SetSecondaryHeaterMode(state ? 2 : 0);
                    break;
                default:
                    throw new InvalidOperationException("Use SetSwitchValue for analog indices.");
            }
        }

        // Analog indices
        public double GetSwitchValue(short id) => id switch
        {
            2 => _tcc.SecondaryHeaterPowerPercent / 100.0,
            3 => (_tcc.SecondaryHeaterSetpointC + 10.0) / 20.0, // map -10..+10 -> 0..1
            4 => _tcc.Dew1PowerPercent / 100.0,
            5 => _tcc.Dew2PowerPercent / 100.0,
            _ => throw new InvalidOperationException("Index is boolean.")
        };

        public void SetSwitchValue(short id, double value)
        {
            if (value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
            switch (id)
            {
                case 2:
                    _tcc.SetSecondaryHeaterPowerPercent((int)Math.Round(value * 100.0));
                    break;
                case 3:
                    _tcc.SetSecondaryHeaterSetpointC(value * 20.0 - 10.0);
                    break;
                case 4:
                    _tcc.SetDew1PowerPercent((int)Math.Round(value * 100.0));
                    break;
                case 5:
                    _tcc.SetDew2PowerPercent((int)Math.Round(value * 100.0));
                    break;
                default:
                    throw new InvalidOperationException("Index is boolean.");
            }
        }
    }
}