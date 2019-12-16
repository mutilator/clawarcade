using InternetClawMachine.Games.GameHelpers;
using System;
using System.Threading.Tasks;

namespace InternetClawMachine.Hardware.ClawControl
{
    internal class U421Module : IMachineControl
    {
        /// <summary>
        /// in ms, how long it takes the crane to fully drop and return to home position
        /// </summary>
        internal int ReturnHomeTime = 20000;

        private int _device = -1;
        private byte _lastDirection = DirectionStop;

        /// <summary>
        /// Pins for movement
        /// </summary>
        private const byte DirectionStop = 0xFF;

        private const byte DirectionForward = 0xFE;
        private const byte DirectionBackward = 0xFD;
        private const byte DirectionLeft = 0xFB;
        private const byte DirectionRight = 0xF7;
        private const byte DirectionDrop = 0xEF;
        private const byte DirectionUp = 0x7F;
        private const byte ConveyorOn = 0x7F;
        private const byte Laser = 0x00;
        private const byte Coinop = 0xDF;
        private const byte FullClawPower = 00;
        private const byte LightPort = 0xBF;
        private bool _sp;

        #region MachineControl Members

        public bool IsClawPlayActive { get; set; }

        public event EventHandler OnBreakSensorTripped;

        public event EventHandler OnReturnedHome;

        public event EventHandler OnClawDropping;

        public event EventHandler OnResetButtonPressed;

        private bool _lightsOn = true;
        private bool _fullClawPower;
        private bool _laserEnabled;
        private bool _conveyorEnabled;
        private bool _alreadyTripped;
        private object _readingInputs = new object();

        public bool Init()
        {
            lock (_readingInputs)
            {
                try
                {
                    if (!UsBm.USBm_FindDevices())
                    {
                        return false;
                    }  // implied else
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }

                // return the number of devices
                // public static extern int USBm_NumberOfDevices();
                var totalDevices = UsBm.USBm_NumberOfDevices();
                _device = totalDevices - 1;  // only One device is ever attached so ...
                IsConnected = true;
            }
            lock (_readingInputs)
            {
                //USBm.USBm_WriteA(_device, 0xFF);
            }
            //make sure i disable all the button on the form so no one can use it
            Reset();

            StartSensorPoller();

            return true;
        }

        public async void StartSensorPoller()
        {
            if (_sp) //if sensor poller is true it means we're already doing this... so don't let it run again
                return;
            _sp = true;
            try
            {
                while (_sp)
                {
                    await Task.Delay(100);
                    if (!IsConnected)
                        break;

                    CheckResetButton();

                    var tripped = ReadBreakSensor();
                    if (tripped && OnBreakSensorTripped != null && !_alreadyTripped)
                    {
                        Console.WriteLine("TRIPPED");
                        _alreadyTripped = true;
                        OnBreakSensorTripped(this, new EventArgs());
                    }
                    else if (tripped && _alreadyTripped)
                    {
                        //dont fire again, placeholder event
                    }
                    else //clear tripped event if it's no longer tripped
                    {
                        _alreadyTripped = false;
                    }
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
            finally
            {
                _sp = false;
            }
        }

        public bool ReadBreakSensor()
        {
            if (!IsConnected)
                return false;
            var data = new byte[1];
            lock (_readingInputs)
            {
                UsBm.USBm_ReadB(_device, data);
            }

            if ((byte)(data[0] | 0xFE) != 0xFE)
                return true;

            return false;
        }

        public void CheckResetButton()
        {
            if (!IsConnected)
                return;
            var data = new byte[1];
            lock (_readingInputs)
            {
                UsBm.USBm_ReadB(_device, data);
            }

            if ((byte)(data[0] | 0xFE) != 0xEE)
                OnResetButtonPressed?.Invoke(this, new EventArgs());

            
        }

        private void WriteMachineData()
        {
            var data = _lastDirection;
            if (_lightsOn) //if lights are on, then don't turn off when passing direction
                data &= LightPort;

            if (_fullClawPower)
                data &= FullClawPower;

            if (_laserEnabled)
                data &= Laser;

            if (_conveyorEnabled)
                data &= ConveyorOn;

            try
            {
                lock (_readingInputs)
                {
                    UsBm.USBm_WriteA(_device, data);
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                Logger.WriteLog(Logger.ErrorLog, error);
            }
        }

        public void ToggleLaser(bool on)
        {
            _laserEnabled = on;
            WriteMachineData();
        }

        public void SetClawPower(int percent)
        {
            if (percent == 100)
            {
                _fullClawPower = true;
            }
            else
            {
                _fullClawPower = false;
            }
            WriteMachineData();
        }

        public void LightSwitch(bool on)
        {
            _lightsOn = on;
            WriteMachineData();
        }

        public async void InsertCoinAsync()
        {
            await Move(MovementDirection.COIN, 200);
        }

        public async Task RunConveyor(int duration)
        {
            _conveyorEnabled = true;
            WriteMachineData();
            await Task.Delay(duration);
            _conveyorEnabled = false;
            WriteMachineData();
        }

        public void RunConveyorSticky(bool run)
        {
            _conveyorEnabled = run;
            WriteMachineData();
        }

        public async Task StopMove()
        {
            await Move(MovementDirection.STOP, 0);
        }

        public async Task MoveForward(int duration)
        {
            await Move(MovementDirection.FORWARD, duration);
        }

        public async Task MoveBackward(int duration)
        {
            await Move(MovementDirection.BACKWARD, duration);
        }

        public async Task MoveLeft(int duration)
        {
            await Move(MovementDirection.LEFT, duration);
        }

        public async Task MoveRight(int duration)
        {
            await Move(MovementDirection.RIGHT, duration);
        }

        public async Task MoveDown(int duration)
        {
            await Move(MovementDirection.DOWN, duration);
        }

        public async Task MoveUp(int duration)
        {
            await Move(MovementDirection.UP, duration);
        }

        public async Task PressDrop()
        {
            //spawn a separate thread to throw the claw return home event
            OnClawDropping?.Invoke(this, new EventArgs());
            ExecuteReturnHomeEvent();
            await Move(MovementDirection.DOWN, 800);
            await Task.Delay(200); //if you hold drop it will cause the machine to lock up, wait 1000ms
            await Move(MovementDirection.DOWN, 800);
            await Task.Delay(200); //if you hold drop it will cause the machine to lock up, wait 1000ms
            await Move(MovementDirection.DOWN, 800);
            await Task.Delay(200); //if you hold drop it will cause the machine to lock up, wait 1000ms
            await Move(MovementDirection.DOWN, 800);
            await Task.Delay(200); //if you hold drop it will cause the machine to lock up, wait 1000ms
        }

        public void ExecuteReturnHomeEvent()
        {
            //async task reset the claw
            Task.Run(async delegate ()
            {
                await Task.Delay(ReturnHomeTime);
                OnReturnedHome?.Invoke(null, new EventArgs());
            });
        }

        public MovementDirection CurrentDirection
        {
            get
            {
                switch (_lastDirection)
                {
                    case DirectionForward:
                        return MovementDirection.FORWARD;

                    case DirectionBackward:
                        return MovementDirection.BACKWARD;

                    case DirectionLeft:
                        return MovementDirection.LEFT;

                    case DirectionRight:
                        return MovementDirection.RIGHT;

                    case DirectionDrop:
                        return MovementDirection.DOWN;

                    case Coinop:
                        return MovementDirection.COIN;

                    case ConveyorOn:
                        return MovementDirection.CONVEYOR;
                    //case DIRECTION_STOP:
                    default:
                        return MovementDirection.STOP;
                }
            }
            set
            {
                switch (value)
                {
                    case MovementDirection.FORWARD:
                        _lastDirection = DirectionForward;
                        break;

                    case MovementDirection.BACKWARD:
                        _lastDirection = DirectionBackward;
                        break;

                    case MovementDirection.LEFT:
                        _lastDirection = DirectionLeft;
                        break;

                    case MovementDirection.RIGHT:
                        _lastDirection = DirectionRight;
                        break;

                    case MovementDirection.UP:
                        _lastDirection = DirectionUp;
                        break;

                    case MovementDirection.DOWN:
                        _lastDirection = DirectionDrop;
                        break;

                    case MovementDirection.STOP:
                        _lastDirection = DirectionStop;
                        break;

                    case MovementDirection.COIN:
                        _lastDirection = Coinop;
                        break;

                    case MovementDirection.CONVEYOR:
                        _lastDirection = ConveyorOn;
                        break;
                }
            }
        }

        public async Task Move(MovementDirection enumDir, int duration, bool force = false)
        {
            if (IsConnected)
            {
                byte dir = 0x0;
                switch (enumDir)
                {
                    case MovementDirection.FORWARD:
                        dir = DirectionForward;
                        break;

                    case MovementDirection.BACKWARD:
                        dir = DirectionBackward;
                        break;

                    case MovementDirection.LEFT:
                        dir = DirectionLeft;
                        break;

                    case MovementDirection.RIGHT:
                        dir = DirectionRight;
                        break;

                    case MovementDirection.UP:
                        dir = DirectionUp;
                        break;

                    case MovementDirection.DOWN:
                        dir = DirectionDrop;
                        break;

                    case MovementDirection.STOP:
                        dir = DirectionStop;
                        break;

                    case MovementDirection.COIN:
                        dir = Coinop;
                        break;

                    case MovementDirection.CONVEYOR:
                        dir = ConveyorOn;
                        break;
                }
                if (dir != _lastDirection || force)
                {
                    _lastDirection = dir;
                    //move it
                    WriteMachineData();
                    //wait for movement
                    await Task.Delay(duration);
                    //stop moving
                    dir = DirectionStop;
                    WriteMachineData();
                }
            }
        }

        public bool IsLit => _lightsOn;
        public bool IsConnected { get; set; }

        #endregion MachineControl Members

        /// <summary>
        /// Destruction guarentee's detach, but if you need strict finalization timing, handle that yourself.
        /// </summary>
        ~U421Module()
        {
            _sp = false;
            Detach();
        }

        public void Disconnect()
        {
            _sp = false;
            Detach();
        }

        public void Detach()
        {
            lock (_readingInputs)
            {
                UsBm.USBm_InitPorts(_device);
                UsBm.USBm_CloseDevice(_device);
            }
        }

        public void Reset()
        {
            if (IsConnected)
            {
                lock (_readingInputs)
                {
                    UsBm.USBm_InitPorts(_device);

                    UsBm.USBm_DirectionAOut(_device);
                    UsBm.USBm_DirectionBInPullup(_device);
                    _alreadyTripped = false;
                }
                WriteMachineData();
                WriteMachineData();
            }
        }

        public bool Connect()
        {
            Init();
            return true;
        }

        public void Flipper()
        {
            //no pins available
        }

        public void Strobe(int red, int blue, int green, int strobeCount, int strobeDelay)
        {
            //not implemented
        }

        public void DualStrobe(int red, int blue, int green, int red2, int blue2, int green2, int strobeCount, int strobeDelay)
        {
            throw new NotImplementedException();
        }
    }
}