using System;
using RCOS.Tcc;
using RCOS.DriverCommon;
// Uncomment these when you add the ASCOM references via NuGet or ASCOM Platform installer.
// using ASCOM.DeviceInterface;
// using ASCOM.Utilities;

namespace ASCOM.RCOS
{
    /// <summary>
    /// Minimal skeleton mapping the TccClient to ASCOM IFocuserV3.
    /// To finish: add ASCOM references and implement COM registration attributes
    /// (Guid, ProgId), plus Profile storage for COM port etc.
    /// </summary>
    public class Focuser /* : IFocuserV3 */
    {
        private readonly TccClient _tcc;
        private readonly DriverProfile _profile;
        public const string DriverId = "ASCOM.RCOS.Focuser";
        public string PortName { get => _profile.ComPort; set { _profile.ComPort = value; _profile.Save(); } }
        private readonly object _sync = new();

        public Focuser(string portName)
        {
            _profile = DriverProfile.Load(DeviceType.Focuser, DriverId);
            if (portName is not null) { _profile.ComPort = portName; _profile.Save(); }
            _tcc = new TccClient(_profile.ComPort);
            _tcc.Open();
            _tcc.Ping();
            _tcc.QueryFocuser();
            _tcc.QueryTemperature();
        }

        public void Dispose()
        {
            _tcc.Dispose();
        }

        // --- Typical IFocuserV3 members (uncomment once ASCOM is referenced) ---
        // public short InterfaceVersion => 3;
        // public string Description => "RCOS TCC Focuser (modernized)";
        // public string DriverInfo => $"Firmware: {_tcc.FirmwareVersion}";
        // public string DriverVersion => "0.1.0";
        // public string Name => "RCOS TCC Focuser";

        public bool IsMoving
        {
            get { _tcc.QueryFocuser(); return _tcc.FocuserIsMoving; }
        }

        public int Position
        {
            get { _tcc.QueryFocuser(); return _tcc.FocuserActPos; }
        }

        public int MaxStep => 40000;      // per VB6 RCOS.cls
        public double StepSizeMicrons => 0.5625; // per VB6 RCOS.cls StepSize

        public void Move(int position)
        {
            lock (_sync)
            {
                _tcc.MoveFocuserAbsolute(position);
            }
        }

        public void Halt()
        {
            _tcc.StopFocuser();
        }

        public bool TempComp
        {
            get; set;
        }

        public void SetTempComp(bool enabled, bool manualMode=false)
        {
            _tcc.SetTempComp(enabled, manualMode);
            TempComp = enabled;
        }

        public double AmbientTemperatureCelsius
        {
            get
            {
                _tcc.QueryTemperature();
                // VB6 returned Farenheit raw; legacy code converts if Celcius flag set.
                // Convert here to Celsius.
                return (_tcc.AmbientTempF - 32.0) * 5.0 / 9.0;
            }
        }
    }
}