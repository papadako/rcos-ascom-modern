using System;
using RCOS.Tcc;
// using ASCOM.DeviceInterface;

namespace ASCOM.RCOS
{
    /// <summary>
    /// Minimal skeleton mapping the TccClient to ASCOM IRotatorV3.
    /// </summary>
    public class Rotator /* : IRotatorV3 */
    {
        private readonly TccClient _tcc;

        public Rotator(string portName)
        {
            _tcc = new TccClient(portName);
            _tcc.Open();
            _tcc.Ping();
            _tcc.QueryRotator();
        }

        public void Dispose() => _tcc.Dispose();

        // public short InterfaceVersion => 3;
        // public string Description => "RCOS TCC Rotator (modernized)";
        // public string DriverVersion => "0.1.0";
        // public string Name => "RCOS TCC Rotator";

        public double Position
        {
            get { _tcc.QueryRotator(); return _tcc.RotatorActPosDeg; }
        }

        public bool IsMoving
        {
            get { _tcc.QueryRotator(); return _tcc.RotatorIsMoving; }
        }

        public void Move(double angleDegrees)
        {
            _tcc.MoveRotatorAbsoluteDeg(angleDegrees);
        }

        public void MoveRelative(double deltaDegrees)
        {
            _tcc.MoveRotatorRelativeDeg(deltaDegrees);
        }

        public void Halt()
        {
            // No explicit stop seen in VB6 for rotator; send a status request to collapse motion
            _tcc.QueryRotator();
        }

        public void Home()
        {
            _tcc.HomeRotator();
        }
    }
}