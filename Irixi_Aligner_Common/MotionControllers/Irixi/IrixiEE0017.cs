﻿using Irixi_Aligner_Common.Configuration.MotionController;
using Irixi_Aligner_Common.Interfaces;
using Irixi_Aligner_Common.Message;
using Irixi_Aligner_Common.MotionControllers.Base;
using IrixiStepperControllerHelper;
using System;

namespace Irixi_Aligner_Common.MotionControllers.Irixi
{
    public class IrixiEE0017 : MotionControllerBase<IrixiAxis>
    {
        #region Variables
        /// <summary>
        /// HidReport the messages when the state of the controller changed such as device connected/disconnected
        /// </summary>
        public event EventHandler<string> OnMessageReported;

        /// <summary>
        /// Raise the event while the input state has changed
        /// </summary>
        public event EventHandler<InputIOEventArgs> OnInputStateChanged;
        
        /// <summary>
        /// Raise the event while the hid report has been received
        /// </summary>
        public event EventHandler<DeviceStateReport> OnHIDReportReceived;

        /// <summary>
        /// The instance of the IrixiMotionController class
        /// </summary>
        IrixiMotionController _controller;

        #endregion

        #region Constructors

        public IrixiEE0017(ConfigPhysicalMotionController Config) : base(Config)
        {
            _controller = new IrixiMotionController(Config.Port);
            _controller.OnConnectionStatusChanged += _controller_OnConnectionProgressChanged;
            _controller.OnReportUpdated += _controller_OnReportUpdated;
            _controller.OnInputIOStatusChanged += ((s, e) =>
            {
                OnInputStateChanged?.Invoke(this, e);
            });
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Raise the event while the connection state is changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void _controller_OnConnectionProgressChanged(object sender, ConnectionEventArgs e)
        {
            switch (e.Event)
            {
                case ConnectionEventArgs.EventType.ConnectionSuccess:
                    OnMessageReported?.Invoke(this, "Connected!");
                    this.IsInitialized = true;
                    break;

                case ConnectionEventArgs.EventType.ConnectionLost:
                    OnMessageReported?.Invoke(this, "Connection was lost, retry ...");
                    this.IsInitialized = false;
                    break;

            }
        }

        /// <summary>
        /// Raise the event while the hid report is received
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">The HID HidReport</param>
        private void _controller_OnReportUpdated(object sender, DeviceStateReport e)
        {
            foreach (var state in e.AxisStateCollection)
            {
                var _axis = FindAxisByName(state.AxisIndex.ToString()) as IrixiAxis;
                _axis.AbsPosition = state.AbsPosition;
                _axis.IsHomed = state.IsHomed;
            }

            OnHIDReportReceived?.Invoke(this, e);
        }

        #endregion

        #region Methods

        protected override bool InitProcess()
        {
            _controller.Open();
            if (_controller.IsConnected)
            {
                LogHelper.WriteLine("{0}, firmware version {1}", this, _controller.FirmwareInfo);

                // pass the configurations to the instance of irixi motion controller class
                for (int i = 0; i < this.Count; i++)
                {
                    var axis = this[i.ToString()] as IrixiAxis;
                    var sdkAxis = _controller.AxisCollection[i];

                    sdkAxis.PosAfterHome = 0;
                    sdkAxis.SoftCCWLS = 0;
                    sdkAxis.SoftCWLS = axis.UnitHelper.MaxSteps;
                    sdkAxis.MaxSteps = axis.UnitHelper.MaxSteps;
                    sdkAxis.MaxSpeed = axis.MaxSpeed;
                    sdkAxis.AccelerationSteps = axis.AccelerationSteps;

                    // reverse the drive directoin
                    _controller.ReverseOriginPosition(i, axis.ReverseDriveDirecton);
                }

                return true;
            }
            else
            {
                this.LastError = _controller.LastError;
                return false;
            }
        }

        protected override bool HomeProcess(IAxis Axis)
        {
                bool ret = false;
                IrixiAxis _axis = Axis as IrixiAxis;

                // lock the axis
                if (_axis.Lock())
                {
                    try
                    {
                        // start homing
                        ret = _controller.Home(_axis.AxisIndex);

                        if (ret)
                        {
                            // set the rel position to 0 after homing
                            _axis.ClearRelPosition();

                            ret = true;
                        }
                        else
                        {
                            _axis.LastError = string.Format("sdk reported error, {0}", _controller.LastError);
                            ret = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _axis.LastError = string.Format("{0}", ex.Message);
                        ret = false;
                    }
                    finally
                    {
                        // release the axis
                        _axis.Unlock();
                    }
                }
                else
                {
                    _axis.LastError = "axis is busy";
                    ret = false;
                }

                return ret;
            }

        protected override bool MoveProcess(IAxis Axis, MoveMode Mode, int Speed, int Steps)
        {
            bool ret = false;
            IrixiAxis _axis = Axis as IrixiAxis;

            int target_pos = 0;

            if (_axis.IsHomed == false)
            {
                _axis.LastError = "the axis is not homed";
                return false;
            }

            try
            {

                // Set the move speed
                if (Mode == MoveMode.ABS)
                {
                    target_pos = Math.Abs(Steps);
                }
                else
                {
                    target_pos = _axis.AbsPosition + Steps;
                }

                // Move the the target position
                if (_axis.CheckSoftLimitation(target_pos))
                {
                    ret = _controller.Move(_axis.AxisIndex, Speed, target_pos, IrixiStepperControllerHelper.MoveMode.ABS);

                    if (!ret)
                    {
                        if (_controller.LastError.EndsWith("31"))
                        {
                            // ignore the error 31 which indicates that uesr interrupted the movement
                            ret = true;
                        }
                        else
                        {
                            _axis.LastError = string.Format("sdk reported error code {0}", _controller.LastError);
                            ret = false;
                        }
                    }
                }
                else
                {
                    _axis.LastError = "target position exceeds the limitation.";

                    ret = false;
                }

            }
            catch (Exception ex)
            {
                _axis.LastError = ex.Message;
                ret = false;
            }

            finally
            {
                // release the axis
                //_axis.Unlock();
            }

            return ret;
        }

        public override void Stop()
        {
            if (this.IsInitialized)
                _controller.Stop(-1);  // stop all, whatever it is moving or not
        }
        
        public void ToggleOutput(int Channel)
        {
            _controller.ToggleGeneralOutput(Channel);
        }
        
        public void SetOutput(int Channel, OutputState State)
        {
            _controller.SetGeneralOutput(Channel, State);
        }

        public override void Dispose()
        {
            try
            {

                _controller.Close();
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                _controller = null;
            }
        }

        #endregion
    }
}
