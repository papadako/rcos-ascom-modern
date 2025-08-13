using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace RCOS.DriverCommon
{
    public enum DeviceType
    {
        Focuser,
        Rotator,
        TemperatureSensor,
        Switch,
        Dew
    }

    /// <summary>
    /// Minimal profile abstraction.
    /// If ASCOM.Utilities.Profile is available, you can optionally compile with USE_ASCOM
    /// and the backend will use the native ASCOM profile store. Otherwise, we persist JSON
    /// in %APPDATA%\RCOS\ASCOM\{DeviceType}\profile.json
    /// </summary>
    public sealed class DriverProfile
    {
        public DeviceType DeviceType { get; }
        public string DriverId { get; }

        // Common settings
        public string ComPort { get; set; } = "COM1";

        // Focuser-specific
        public int FocuserMaxStep { get; set; } = 40000;
        public double FocuserStepSizeMicrons { get; set; } = 0.5625;
        public bool FanAutoOnConnect { get; set; } = false;

        // Reserved for future
        public string? ReservedJson { get; set; }

        public DriverProfile(DeviceType type, string driverId)
        {
            DeviceType = type;
            DriverId = driverId;
        }

        public static DriverProfile Load(DeviceType type, string driverId)
        {
#if USE_ASCOM
            // Native ASCOM profile backend
            using var p = new ASCOM.Utilities.Profile() { DeviceType = type.ToString() };
            var dp = new DriverProfile(type, driverId);
            dp.ComPort = p.GetValue(driverId, nameof(ComPort), string.Empty, dp.ComPort);
            dp.FocuserMaxStep = int.TryParse(p.GetValue(driverId, nameof(FocuserMaxStep), string.Empty, dp.FocuserMaxStep.ToString()), out var ms) ? ms : dp.FocuserMaxStep;
            dp.FocuserStepSizeMicrons = double.TryParse(p.GetValue(driverId, nameof(FocuserStepSizeMicrons), string.Empty, dp.FocuserStepSizeMicrons.ToString()), out var sz) ? sz : dp.FocuserStepSizeMicrons;
            dp.FanAutoOnConnect = bool.TryParse(p.GetValue(driverId, nameof(FanAutoOnConnect), string.Empty, dp.FanAutoOnConnect.ToString()), out var fa) ? fa : dp.FanAutoOnConnect;
            dp.ReservedJson = p.GetValue(driverId, nameof(ReservedJson), string.Empty, dp.ReservedJson ?? "");
            return dp;
#else
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RCOS", "ASCOM", type.ToString());
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "profile.json");
            if (!File.Exists(file))
            {
                var fresh = new DriverProfile(type, driverId);
                File.WriteAllText(file, JsonSerializer.Serialize(fresh, new JsonSerializerOptions { WriteIndented = true }));
                return fresh;
            }
            try
            {
                var json = File.ReadAllText(file);
                var stored = JsonSerializer.Deserialize<DriverProfile>(json);
                if (stored == null) return new DriverProfile(type, driverId);
                stored.DeviceType.Equals(type); // keep
                stored.DriverId.Equals(driverId);
                return stored;
            }
            catch
            {
                return new DriverProfile(type, driverId);
            }
#endif
        }

        public void Save()
        {
#if USE_ASCOM
            using var p = new ASCOM.Utilities.Profile() { DeviceType = DeviceType.ToString() };
            p.WriteValue(DriverId, nameof(ComPort), ComPort);
            p.WriteValue(DriverId, nameof(FocuserMaxStep), FocuserMaxStep.ToString());
            p.WriteValue(DriverId, nameof(FocuserStepSizeMicrons), FocuserStepSizeMicrons.ToString());
            p.WriteValue(DriverId, nameof(FanAutoOnConnect), FanAutoOnConnect.ToString());
            p.WriteValue(DriverId, nameof(ReservedJson), ReservedJson ?? "");
#else
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RCOS", "ASCOM", DeviceType.ToString());
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "profile.json");
            File.WriteAllText(file, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
#endif
        }
    }
}