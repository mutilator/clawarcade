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
        internal int _returnHomeTime = 20000;

        private int _device = -1;
        private byte _lastDirection = DIRECTION_STOP;

        /// <summary>
        /// Pins for movement
        /// </summary>
        private const byte DIRECTION_STOP = 0xFF;

        private const byte DIRECTION_FORWARD = 0xFE;
        private const byte DIRECTION_BACKWARD = 0xFD;
        private const byte DIRECTION_LEFT = 0xFB;
        private const byte DIRECTION_RIGHT = 0xF7;
        private const byte DIRECTION_DROP = 0xEF;
        private const byte DIRECTION_UP = 0x7F;
        private const byte CONVEYOR_ON = 0x7F;
        private const byte LASER = 0x00;
        private const byte COINOP = 0xDF;
        private const byte FULL_CLAW_POWER = 00;
        private const byte LIGHT_PORT = 0xBF;
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
                    if (!USBm.USBm_FindDevices())
                    {
                        return false;
                    }  // implied else
                }
                catch (Exception ex)
                {
                    string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
                    Logger.WriteLog(Logger.ErrorLog, error);
                }

                // return the number of devices
                // public static extern int USBm_NumberOfDevices();
                var TotalDevices = USBm.USBm_NumberOfDevices();
                _device = TotalDevices - 1;  // only One device is ever attached so ...
                IsConnected = true;
            }
            lock (_readingInputs)
            {
                //USBm.USBm_WriteA(_device, 0xFF);
            }
            //make sure i disable all the button on the form so no one can use it
            reset();

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
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
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
            byte[] data = new byte[1];
            lock (_readingInputs)
            {
                USBm.USBm_ReadB(_device, data);
            }

            if ((byte)(data[0] | 0xFE) != 0xFE)
                return true;

            return false;
        }

        private void WriteMachineData()
        {
            var data = _lastDirection;
            if (_lightsOn) //if lights are on, then don't turn off when passing direction
                data &= LIGHT_PORT;

            if (_fullClawPower)
                data &= FULL_CLAW_POWER;

            if (_laserEnabled)
                data &= LASER;

            if (_conveyorEnabled)
                data &= CONVEYOR_ON;

            try
            {
                lock (_readingInputs)
                {
                    USBm.USBm_WriteA(_device, data);
                }
            }
            catch (Exception ex)
            {
                string error = String.Format("ERROR {0} {1}", ex.Message, ex.ToString());
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
                await Task.Delay(_returnHomeTime);
                OnReturnedHome?.Invoke(null, new EventArgs());
            });
        }

        public MovementDirection CurrentDirection
        {
            get
            {
                switch (_lastDirection)
                {
                    case DIRECTION_FORWARD:
                        return MovementDirection.FORWARD;

                    case DIRECTION_BACKWARD:
                        return MovementDirection.BACKWARD;

                    case DIRECTION_LEFT:
                        return MovementDirection.LEFT;

                    case DIRECTION_RIGHT:
                        return MovementDirection.RIGHT;

                    case DIRECTION_DROP:
                        return MovementDirection.DOWN;

                    case COINOP:
                        return MovementDirection.COIN;

                    case CONVEYOR_ON:
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
                        _lastDirection = DIRECTION_FORWARD;
                        break;

                    case MovementDirection.BACKWARD:
                        _lastDirection = DIRECTION_BACKWARD;
                        break;

                    case MovementDirection.LEFT:
                        _lastDirection = DIRECTION_LEFT;
                        break;

                    case MovementDirection.RIGHT:
                        _lastDirection = DIRECTION_RIGHT;
                        break;

                    case MovementDirection.UP:
                        _lastDirection = DIRECTION_UP;
                        break;

                    case MovementDirection.DOWN:
                        _lastDirection = DIRECTION_DROP;
                        break;

                    case MovementDirection.STOP:
                        _lastDirection = DIRECTION_STOP;
                        break;

                    case MovementDirection.COIN:
                        _lastDirection = COINOP;
                        break;

                    case MovementDirection.CONVEYOR:
                        _lastDirection = CONVEYOR_ON;
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
                        dir = DIRECTION_FORWARD;
                        break;

                    case MovementDirection.BACKWARD:
                        dir = DIRECTION_BACKWARD;
                        break;

                    case MovementDirection.LEFT:
                        dir = DIRECTION_LEFT;
                        break;

                    case MovementDirection.RIGHT:
                        dir = DIRECTION_RIGHT;
                        break;

                    case MovementDirection.UP:
                        dir = DIRECTION_UP;
                        break;

                    case MovementDirection.DOWN:
                        dir = DIRECTION_DROP;
                        break;

                    case MovementDirection.STOP:
                        dir = DIRECTION_STOP;
                        break;

                    case MovementDirection.COIN:
                        dir = COINOP;
                        break;

                    case MovementDirection.CONVEYOR:
                        dir = CONVEYOR_ON;
                        break;
                }
                if ((dir != _lastDirection) || (force))
                {
                    _lastDirection = dir;
                    //move it
                    WriteMachineData();
                    //wait for movement
                    await Task.Delay(duration);
                    //stop moving
                    dir = DIRECTION_STOP;
                    WriteMachineData();
                }
            }
        }

        public bool IsLit { get { return _lightsOn; } }
        public bool IsConnected { get; set; }

        #endregion MachineControl Members

        /// <summary>
        /// Destruction guarentee's detach, but if you need strict finalization timing, handle that yourself.
        /// </summary>
        ~U421Module()
        {
            _sp = false;
            detach();
        }

        public void Disconnect()
        {
            _sp = false;
            detach();
        }

        public void detach()
        {
            lock (_readingInputs)
            {
                USBm.USBm_InitPorts(_device);
                USBm.USBm_CloseDevice(_device);
            }
        }

        public void reset()
        {
            if (IsConnected)
            {
                lock (_readingInputs)
                {
                    USBm.USBm_InitPorts(_device);

                    USBm.USBm_DirectionAOut(_device);
                    USBm.USBm_DirectionBInPullup(_device);
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