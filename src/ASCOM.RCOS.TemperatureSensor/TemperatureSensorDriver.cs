using System;
using System.Collections.Generic;
using RCOS.Tcc;
using RCOS.DriverCommon;
// using ASCOM.DeviceInterface;   // ITemperatureSensor
// using ASCOM.Utilities;

namespace ASCOM.RCOS
{
    /// <summary>
    /// Maps TCC temperature tokens (:t1, :t2, :t3, :t7) to an ASCOM ITemperatureSensor.
    /// Sensor indices:
    /// 0 = Ambient, 1 = Primary, 2 = Secondary, 3 = Electronics
    /// </summary>
    public class TemperatureSensor /* : ITemperatureSensor */
    {
        private readonly TccClient _tcc;
        private readonly DriverProfile _profile;
        public const string DriverId = "ASCOM.RCOS.TemperatureSensor";
        public string PortName { get => _profile.ComPort; set { _profile.ComPort = value; _profile.Save(); } }
        private readonly object _sync = new();

        public TemperatureSensor(string portName)
        {
            _profile = DriverProfile.Load(DeviceType.TemperatureSensor, DriverId);
            if (portName is not null) { _profile.ComPort = portName; _profile.Save(); }
            _tcc = new TccClient(_profile.ComPort);
            _tcc.Open();
            _tcc.QueryTemperature();
        }

        public void Dispose() => _tcc.Dispose();

        // public short InterfaceVersion => 1;
        // public string Description => "RCOS TCC Temperature Sensor";
        // public string DriverVersion => "0.1.0";
        // public string Name => "RCOS TCC Temperature Sensor";

        public IList<string> SensorNames => new[] { "Ambient", "Primary", "Secondary", "Electronics" };

        public double ReadCelsius(int sensorIndex)
        {
            lock (_sync)
            {
                _tcc.QueryTemperature();
                return sensorIndex switch
                {
                    0 => FtoC(_tcc.AmbientTempF),
                    1 => FtoC(_tcc.PrimaryTempF),
                    2 => FtoC(_tcc.SecondaryTempF),
                    3 => FtoC(_tcc.ElectronicsTempF),
                    _ => throw new ArgumentOutOfRangeException(nameof(sensorIndex))
                };
            }
        }

        public (double ambientC, double primaryC, double secondaryC, double electronicsC) ReadAllCelsius()
        {
            lock (_sync)
            {
                _tcc.QueryTemperature();
                return (FtoC(_tcc.AmbientTempF),
                        FtoC(_tcc.PrimaryTempF),
                        FtoC(_tcc.SecondaryTempF),
                        FtoC(_tcc.ElectronicsTempF));
            }
        }

        private static double FtoC(double f) => (f - 32.0) * (5.0 / 9.0);
    }
}