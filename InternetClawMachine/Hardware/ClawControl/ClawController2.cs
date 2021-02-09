using InternetClawMachine.Games.GameHelpers;
using InternetClawMachine.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InternetClawMachine.Hardware.ClawControl
{
    /// <summary>
    /// A simpler implementation of the controller, used for a controller that has far less functionality and feedback directly form the machine
    /// </summary>
    internal class ClawController2 : ClawController
    {

        public ClawController2(ClawMachine c) : base(c)
        {
            
        }

        override internal async Task Move(MovementDirection enumDir, int duration, bool force = false)
        {
            if (IsConnected)
            {
                var dir = "s";
                switch (enumDir)
                {
                    case MovementDirection.FORWARD:
                        dir = "f";
                        break;

                    case MovementDirection.BACKWARD:
                        dir = "b";
                        break;

                    case MovementDirection.LEFT:
                        dir = "l";
                        break;

                    case MovementDirection.RIGHT:
                        dir = "r";
                        break;

                    case MovementDirection.UP:
                        dir = "u";
                        break;

                    case MovementDirection.DOWN:
                        dir = "dn";
                        break;

                    case MovementDirection.CLAWCLOSE:
                        dir = "claw";
                        break;

                    case MovementDirection.DROP:
                        Task.Run(async delegate
                        {
                            await Task.Delay(15000);
                            FireCenteredEvent();
                            
                        });
                        IsClawPlayActive = true;
                        dir = "d";
                        break;

                    case MovementDirection.STOP:
                        dir = "s";
                        break;

                    case MovementDirection.COIN:
                        dir = "start";
                        break;

                    case MovementDirection.CONVEYOR:
                        dir = "belt";
                        break;
                }
                _lastDirection = dir;
                SendCommandAsync(dir + " " + duration);
                if (duration > 0)
                {
                    var guid = Guid.NewGuid();
                    Logger.WriteLog(Logger.DebugLog, guid + " sleeping: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.TRACE);
                    await Task.Delay(duration);
                    Logger.WriteLog(Logger.DebugLog, guid + " woke: " + Thread.CurrentThread.ManagedThreadId, Logger.LogLevel.TRACE);
                }
            }
        }

        override public void InsertCoinAsync()
        {
            Task.Run(async delegate
            {
                await Move(MovementDirection.COIN, 0);
                await Move(MovementDirection.FORWARD, 1500);
                await Move(MovementDirection.RIGHT, 1500);
            });
        }

        override public async Task MoveBackward(int duration)
        {
            await Move(MovementDirection.BACKWARD, duration);
        }

        override public async Task MoveDown(int duration)
        {
            await Move(MovementDirection.DOWN, duration);
        }

        override public async Task MoveForward(int duration)
        {
            await Move(MovementDirection.FORWARD, duration);
        }

        override public async Task MoveLeft(int duration)
        {
            await Move(MovementDirection.LEFT, duration);
        }

        override public async Task MoveRight(int duration)
        {
            await Move(MovementDirection.RIGHT, duration);
        }

        override public async Task PressDrop()
        {
            await Move(MovementDirection.DROP, 0);
        }

        override public async Task RunConveyor(int runtime)
        {
            if (!IsConnected)
                return;
            SendCommandAsync("belt " + runtime);
            await Task.Delay(runtime);
        }

        override public void SetClawPower(int percent)
        {
            if (!IsConnected)
                return;
            var power = (int)((double)(100 - percent) / 100 * 255);
            var str = string.Format("uno p {0}", power);
            SendCommandAsync(str);
        }

        override public async Task StopMove()
        {
            await Move(MovementDirection.STOP, 0);
        }

        override public void Strobe(int red, int blue, int green, int strobeCount, int strobeDelay)
        {
            SendCommandAsync($"strobe {red} {blue} {green} {strobeCount} {strobeDelay} 0");
        }

        override public void DualStrobe(int red, int blue, int green, int red2, int blue2, int green2, int strobeCount, int strobeDelay)
        {
            SendCommandAsync($"uno ds {red}:{blue}:{green} {red2}:{blue2}:{green2} {strobeCount} {strobeDelay} 0");
        }
    }

}