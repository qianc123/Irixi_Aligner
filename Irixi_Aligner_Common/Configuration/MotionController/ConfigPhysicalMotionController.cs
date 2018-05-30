﻿using Irixi_Aligner_Common.Configuration.Base;

namespace Irixi_Aligner_Common.Configuration.MotionController
{
    public class ConfigPhysicalMotionController : ConfigurationBase
    {
        /// <summary>
        /// Get the model of controller defined by ControllerType
        /// </summary>
        public MotionControllerType Model { get; set; }

        /// <summary>
        /// Get the collection of the axes controled by the controller
        /// </summary>
        public ConfigPhysicalAxis[] AxisCollection { get; set; }

        public override string ToString()
        {
            return string.Format("{0}\\{1} - {2}", this.DeviceClass, this.Port, this.Comment);
        }
    }
}
