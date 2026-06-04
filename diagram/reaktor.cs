REAKTOR

// dCom/dCom/RtuCfg.txt

STA 22
TCP 31313
DBC 1

DO_REG  1  5300  0  0    1   0  DO  @Rashlad1    1  1
DO_REG  1  5301  0  0    1   0  DO  @Rashlad2    1  1
DO_REG  1  5302  0  0    1   0  DO  @Rashlad3    1  1
DO_REG  1  5303  0  0    1   0  DO  @GlavniR     1  1
DO_REG  1  5304  0  0    1   1  DO  @PomocniR    1  0
HR_INT  1  2300  0  0  100  50  AO  @TempJezgra  1  1  0  85  100
DO_REG  1  4300  0  0    1   1  DO  @Izvor1      1  0
DO_REG  1  4301  0  0    1   0  DO  @Izvor2      1  1



// ConfigItem.cs
using Common;
using System;
using System.Collections.Generic;

namespace dCom.Configuration
{
    internal class ConfigItem : IConfigItem
	{
		#region Fields

		private PointType registryType;
		private ushort numberOfRegisters;
		private ushort startAddress;
		private ushort decimalSeparatorPlace;
		private ushort minValue;
		private ushort maxValue;
		private ushort defaultValue;
		private string processingType;
		private string description;
		private int acquisitionInterval;
		private double scalingFactor;
		private double deviation;
		private double egu_max;
		private double egu_min;
		private ushort abnormalValue;
		private double highLimit;
		private double lowLimit;
        private int secondsPassedSinceLastPoll;

		#endregion Fields

		#region Properties

		public PointType RegistryType
		{
			get { return registryType; }
			set { registryType = value; }
		}

		public ushort NumberOfRegisters
		{
			get { return numberOfRegisters; }
			set { numberOfRegisters = value; }
		}

		public ushort StartAddress
		{
			get { return startAddress; }
			set { startAddress = value; }
		}

		public ushort DecimalSeparatorPlace
		{
			get { return decimalSeparatorPlace; }
			set { decimalSeparatorPlace = value; }
		}

		public ushort MinValue
		{
			get { return minValue; }
			set { minValue = value; }
		}

		public ushort MaxValue
		{
			get { return maxValue; }
			set { maxValue = value; }
		}

		public ushort DefaultValue
		{
			get { return defaultValue; }
			set { defaultValue = value; }
		}

		public string ProcessingType
		{
			get { return processingType; }
			set { processingType = value; }
		}

		public string Description
		{
			get { return description; }
			set { description = value; }
		}

		public int AcquisitionInterval
		{
			get { return acquisitionInterval; }
			set { acquisitionInterval = value; }
		}

		public double ScaleFactor
		{
			get { return scalingFactor; }
			set { scalingFactor = value; }
		}

		public double Deviation
		{
			get { return deviation; }
			set { deviation = value; }
		}

		public double EGU_Max
		{
			get { return egu_max; }
			set { egu_max = value; }
		}

		public double EGU_Min
		{
			get { return egu_min; }
			set { egu_min = value; }
		}

		public ushort AbnormalValue
		{
			get { return abnormalValue; }
			set { abnormalValue = value; }
		}

		public double HighLimit
		{
			get { return highLimit; }
			set { highLimit = value; }
		}

		public double LowLimit
		{
			get { return lowLimit; }
			set { lowLimit = value; }
		}

        public int SecondsPassedSinceLastPoll
        {
            get { return secondsPassedSinceLastPoll; }
            set { secondsPassedSinceLastPoll = value; }
        }

        #endregion Properties

        public ConfigItem(List<string> configurationParameters)
		{
			RegistryType = GetRegistryType(configurationParameters[0]);

			int temp;
			double doubleTemp;

			Int32.TryParse(configurationParameters[1], out temp);
			NumberOfRegisters = (ushort)temp;

			Int32.TryParse(configurationParameters[2], out temp);
			StartAddress = (ushort)temp;

			Int32.TryParse(configurationParameters[3], out temp);
			DecimalSeparatorPlace = (ushort)temp;

			Int32.TryParse(configurationParameters[4], out temp);
			MinValue = (ushort)temp;

			Int32.TryParse(configurationParameters[5], out temp);
			MaxValue = (ushort)temp;

			Int32.TryParse(configurationParameters[6], out temp);
			DefaultValue = (ushort)temp;

			ProcessingType = configurationParameters[7];
			Description = configurationParameters[8].TrimStart('@');

            if (configurationParameters[9].Equals("#"))
            {
                AcquisitionInterval = 1;
            }
            else
            {
                Int32.TryParse(configurationParameters[9], out temp);
                AcquisitionInterval = temp;
            }

            ScaleFactor = 1;
            Deviation = 0;

            bool isAnalog = RegistryType == PointType.ANALOG_INPUT
                         || RegistryType == PointType.ANALOG_OUTPUT
                         || RegistryType == PointType.HR_LONG;

            if (isAnalog)
            {
                LowLimit = double.MinValue;
                HighLimit = 85;
                EGU_Max = 100;

                if (configurationParameters.Count > 10 && ParseDouble(configurationParameters[10], out doubleTemp))
                {
                    ScaleFactor = doubleTemp;
                }

                if (configurationParameters.Count > 11 && ParseDouble(configurationParameters[11], out doubleTemp))
                {
                    Deviation = doubleTemp;
                }

                if (configurationParameters.Count > 12 && ParseDouble(configurationParameters[12], out doubleTemp))
                {
                    HighLimit = doubleTemp;
                }

                if (configurationParameters.Count > 13 && ParseDouble(configurationParameters[13], out doubleTemp))
                {
                    EGU_Max = doubleTemp;
                }
            }
            else
            {
                AbnormalValue = (ushort)(DefaultValue == 0 ? 1 : 0);

                if (configurationParameters.Count > 10)
                {
                    Int32.TryParse(configurationParameters[10], out temp);
                    AbnormalValue = (ushort)temp;
                }
            }
        }

        private static bool ParseDouble(string value, out double result)
        {
            return Double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out result);
        }

		private PointType GetRegistryType(string registryTypeName)
		{
			PointType registryType;

			switch (registryTypeName)
			{
				case "DO_REG":
					registryType = PointType.DIGITAL_OUTPUT;
					break;

				case "DI_REG":
					registryType = PointType.DIGITAL_INPUT;
					break;

				case "IN_REG":
					registryType = PointType.ANALOG_INPUT;
					break;

				case "HR_INT":
					registryType = PointType.ANALOG_OUTPUT;
					break;

				default:
					registryType = PointType.HR_LONG;
					break;
			}

			return registryType;
		}
	}
}


// EGUConverter.cs
using System;

namespace ProcessingModule
{
    public class EGUConverter
	{
		public double ConvertToEGU(double scalingFactor, double deviation, ushort rawValue)
		{
            return scalingFactor * rawValue + deviation;
		}

		public ushort ConvertToRaw(double scalingFactor, double deviation, double eguValue)
        {
            if (scalingFactor == 0)
            {
                return (ushort)eguValue;
            }

            return (ushort)((eguValue - deviation) / scalingFactor);
		}
	}
}


//AlarmProcessor.cs

using Common;

namespace ProcessingModule
{
    public class AlarmProcessor
	{
		public AlarmType GetAlarmForAnalogPoint(double eguValue, IConfigItem configItem)
		{
			if (eguValue > configItem.HighLimit)
			{
				return AlarmType.HIGH_ALARM;
			}

			if (eguValue < configItem.LowLimit)
			{
				return AlarmType.LOW_ALARM;
			}

			return AlarmType.NO_ALARM;
		}

		public AlarmType GetAlarmForDigitalPoint(ushort state, IConfigItem configItem)
		{
            if (state == configItem.AbnormalValue)
            {
                return AlarmType.ABNORMAL_VALUE;
            }

            return AlarmType.NO_ALARM;
        }
	}
}

//ProcessingManager.cs

using Common;
using Modbus;
using Modbus.FunctionParameters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcessingModule
{
    public class ProcessingManager : IProcessingManager
    {
        private IFunctionExecutor functionExecutor;
        private IStorage storage;
        private AlarmProcessor alarmProcessor;
        private EGUConverter eguConverter;

        public ProcessingManager(IStorage storage, IFunctionExecutor functionExecutor)
        {
            this.storage = storage;
            this.functionExecutor = functionExecutor;
            this.alarmProcessor = new AlarmProcessor();
            this.eguConverter = new EGUConverter();
            this.functionExecutor.UpdatePointEvent += CommandExecutor_UpdatePointEvent;
        }

        public void ExecuteReadCommand(IConfigItem configItem, ushort transactionId, byte remoteUnitAddress, ushort startAddress, ushort numberOfPoints)
        {
            ModbusReadCommandParameters p = new ModbusReadCommandParameters(
                6,
                (byte)GetReadFunctionCode(configItem.RegistryType),
                startAddress,
                numberOfPoints,
                transactionId,
                remoteUnitAddress);

            IModbusFunction fn = FunctionFactory.CreateModbusFunction(p);
            this.functionExecutor.EnqueueCommand(fn);
        }

        public void ExecuteWriteCommand(IConfigItem configItem, ushort transactionId, byte remoteUnitAddress, ushort pointAddress, int value)
        {
            if (configItem.RegistryType == PointType.ANALOG_OUTPUT)
            {
                ExecuteAnalogCommand(configItem, transactionId, remoteUnitAddress, pointAddress, value);
            }
            else
            {
                ExecuteDigitalCommand(configItem, transactionId, remoteUnitAddress, pointAddress, value);
            }
        }

        private void ExecuteDigitalCommand(IConfigItem configItem, ushort transactionId, byte remoteUnitAddress, ushort pointAddress, int value)
        {
            ModbusWriteCommandParameters p = new ModbusWriteCommandParameters(
                6,
                (byte)ModbusFunctionCode.WRITE_SINGLE_COIL,
                pointAddress,
                (ushort)value,
                transactionId,
                remoteUnitAddress);

            IModbusFunction fn = FunctionFactory.CreateModbusFunction(p);
            this.functionExecutor.EnqueueCommand(fn);
        }

        private void ExecuteAnalogCommand(IConfigItem configItem, ushort transactionId, byte remoteUnitAddress, ushort pointAddress, int value)
        {
            ModbusWriteCommandParameters p = new ModbusWriteCommandParameters(
                6,
                (byte)ModbusFunctionCode.WRITE_SINGLE_REGISTER,
                pointAddress,
                (ushort)value,
                transactionId,
                remoteUnitAddress);

            IModbusFunction fn = FunctionFactory.CreateModbusFunction(p);
            this.functionExecutor.EnqueueCommand(fn);
        }

        private ModbusFunctionCode? GetReadFunctionCode(PointType registryType)
        {
            switch (registryType)
            {
                case PointType.DIGITAL_OUTPUT:
                    return ModbusFunctionCode.READ_COILS;

                case PointType.DIGITAL_INPUT:
                    return ModbusFunctionCode.READ_DISCRETE_INPUTS;

                case PointType.ANALOG_INPUT:
                    return ModbusFunctionCode.READ_INPUT_REGISTERS;

                case PointType.ANALOG_OUTPUT:
                    return ModbusFunctionCode.READ_HOLDING_REGISTERS;

                case PointType.HR_LONG:
                    return ModbusFunctionCode.READ_HOLDING_REGISTERS;

                default:
                    return null;
            }
        }

        private void CommandExecutor_UpdatePointEvent(PointType type, ushort pointAddress, ushort newValue)
        {
            List<IPoint> points = storage.GetPoints(
                new List<PointIdentifier>(1) { new PointIdentifier(type, pointAddress) });

            if (type == PointType.ANALOG_INPUT || type == PointType.ANALOG_OUTPUT)
            {
                ProcessAnalogPoint(points.First() as IAnalogPoint, newValue);
            }
            else
            {
                ProcessDigitalPoint(points.First() as IDigitalPoint, newValue);
            }
        }

        private void ProcessDigitalPoint(IDigitalPoint point, ushort newValue)
        {
            point.RawValue = newValue;
            point.Timestamp = DateTime.Now;
            point.State = (DState)newValue;
            point.Alarm = alarmProcessor.GetAlarmForDigitalPoint(newValue, point.ConfigItem);
        }

        private void ProcessAnalogPoint(IAnalogPoint point, ushort newValue)
        {
            point.RawValue = newValue;
            point.Timestamp = DateTime.Now;

            double eguValue = eguConverter.ConvertToEGU(
                point.ConfigItem.ScaleFactor,
                point.ConfigItem.Deviation,
                newValue);

            point.EguValue = eguValue;
            point.Alarm = alarmProcessor.GetAlarmForAnalogPoint(eguValue, point.ConfigItem);
        }

        public void InitializePoint(PointType type, ushort pointAddress, ushort defaultValue)
        {
            List<IPoint> points = storage.GetPoints(
                new List<PointIdentifier>(1) { new PointIdentifier(type, pointAddress) });

            if (type == PointType.ANALOG_INPUT || type == PointType.ANALOG_OUTPUT)
            {
                ProcessAnalogPoint(points.First() as IAnalogPoint, defaultValue);
            }
            else
            {
                ProcessDigitalPoint(points.First() as IDigitalPoint, defaultValue);
            }
        }
    }
}

// Analogoutput.cs

using Common;
using ProcessingModule;
using System;

namespace dCom.ViewModel
{
    internal class AnalogOutput : AnalogBase
	{
		private EGUConverter eguConverter = new EGUConverter();

		public AnalogOutput(IConfigItem c, IProcessingManager processingManager, IStateUpdater stateUpdater, IConfiguration configuration, int i)
			: base(c, processingManager, stateUpdater, configuration, i)
		{
		}

		protected override bool WriteCommand_CanExecute(object obj)
		{
            return !(CommandedValue < configItem.MinValue || CommandedValue > ConfigItem.MaxValue);
		}

        protected override void WriteCommand_Execute(object obj)
        {
            try
            {
                ushort rawValue = eguConverter.ConvertToRaw(
                    ConfigItem.ScaleFactor,
                    ConfigItem.Deviation,
                    CommandedValue);

                this.processingManager.ExecuteWriteCommand(
                    ConfigItem,
                    configuration.GetTransactionId(),
                    configuration.UnitAddress,
                    address,
                    rawValue);
            }
            catch (Exception ex)
            {
                string message = $"{ex.TargetSite.ReflectedType.Name}.{ex.TargetSite.Name}: {ex.Message}";
                this.stateUpdater.LogMessage(message);
            }
        }
    }
}

//DigitalOutput.cs

using Common;
using System;
using System.Collections.Generic;

namespace dCom.ViewModel
{
    internal class DigitalOutput : DigitalBase
	{
		public DigitalOutput(IConfigItem c, IProcessingManager processingManager, IStateUpdater stateUpdater, IConfiguration configuration, int i)
			: base(c, processingManager, stateUpdater, configuration, i)
		{
		}

		protected override bool WriteCommand_CanExecute(object obj)
		{
			return !(CommandedValue < 0 || CommandedValue > 1);
		}

        protected override void WriteCommand_Execute(object obj)
        {
            try
            {
                if ((address == 4300 || address == 4301) && (int)CommandedValue == 1 && !IsTemperatureBelowHighAlarm())
                {
                    this.stateUpdater.LogMessage("Komanda odbijena: temperatura jezgra nije ispod HighAlarm vrednosti.");
                    return;
                }

                this.processingManager.ExecuteWriteCommand(
                    ConfigItem,
                    configuration.GetTransactionId(),
                    configuration.UnitAddress,
                    address,
                    (int)CommandedValue);
            }
            catch (Exception ex)
            {
                string message = $"{ex.TargetSite.ReflectedType.Name}.{ex.TargetSite.Name}: {ex.Message}";
                this.stateUpdater.LogMessage(message);
            }
        }

        private bool IsTemperatureBelowHighAlarm()
        {
            IStorage storage = stateUpdater as IStorage;

            if (storage == null)
            {
                return true;
            }

            IConfigItem temperatureConfig = null;

            foreach (IConfigItem ci in configuration.GetConfigurationItems())
            {
                if (ci.RegistryType == PointType.ANALOG_OUTPUT)
                {
                    temperatureConfig = ci;
                    break;
                }
            }

            if (temperatureConfig == null)
            {
                return true;
            }

            List<IPoint> points = storage.GetPoints(
                new List<PointIdentifier>(1)
                {
                    new PointIdentifier(PointType.ANALOG_OUTPUT, temperatureConfig.StartAddress)
                });

            if (points.Count == 0)
            {
                return true;
            }

            IAnalogPoint temperaturePoint = points[0] as IAnalogPoint;

            if (temperaturePoint == null)
            {
                return true;
            }

            return temperaturePoint.EguValue < temperatureConfig.HighLimit;
        }
    }
}

// AutomationManager.cs

using Common;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ProcessingModule
{
    public class AutomationManager : IAutomationManager, IDisposable
	{
		private Thread automationWorker;
        private AutoResetEvent automationTrigger;
        private IStorage storage;
		private IProcessingManager processingManager;
		private int delayBetweenCommands;
        private IConfiguration configuration;

        private const ushort TEMP_ADDRESS = 2300;

        private const ushort I1_ADDRESS = 4300;
        private const ushort I2_ADDRESS = 4301;

        private const ushort T1_ADDRESS = 5300;
        private const ushort T2_ADDRESS = 5301;
        private const ushort T3_ADDRESS = 5302;
        private const ushort T4_ADDRESS = 5303;
        private const ushort T5_ADDRESS = 5304;

        private EGUConverter eguConverter = new EGUConverter();

        public AutomationManager(IStorage storage, IProcessingManager processingManager, AutoResetEvent automationTrigger, IConfiguration configuration)
		{
			this.storage = storage;
			this.processingManager = processingManager;
            this.configuration = configuration;
            this.automationTrigger = automationTrigger;
        }

		private void InitializeAndStartThreads()
		{
			InitializeAutomationWorkerThread();
			StartAutomationWorkerThread();
		}

		private void InitializeAutomationWorkerThread()
		{
			automationWorker = new Thread(AutomationWorker_DoWork);
			automationWorker.Name = "Automation Thread";
		}

		private void StartAutomationWorkerThread()
		{
			automationWorker.Start();
		}

		private void AutomationWorker_DoWork()
		{
			while (!disposedValue)
			{
				automationTrigger.WaitOne();

				IConfigItem tempCfg = GetConfigItem(TEMP_ADDRESS);
				IConfigItem i1Cfg = GetConfigItem(I1_ADDRESS);
				IConfigItem i2Cfg = GetConfigItem(I2_ADDRESS);
				IConfigItem t4Cfg = GetConfigItem(T4_ADDRESS);
				IConfigItem t5Cfg = GetConfigItem(T5_ADDRESS);

				IAnalogPoint tempPoint = GetPoint(PointType.ANALOG_OUTPUT, TEMP_ADDRESS) as IAnalogPoint;

				IDigitalPoint t1Point = GetPoint(PointType.DIGITAL_OUTPUT, T1_ADDRESS) as IDigitalPoint;
				IDigitalPoint t2Point = GetPoint(PointType.DIGITAL_OUTPUT, T2_ADDRESS) as IDigitalPoint;
				IDigitalPoint t3Point = GetPoint(PointType.DIGITAL_OUTPUT, T3_ADDRESS) as IDigitalPoint;
				IDigitalPoint t4Point = GetPoint(PointType.DIGITAL_OUTPUT, T4_ADDRESS) as IDigitalPoint;
				IDigitalPoint t5Point = GetPoint(PointType.DIGITAL_OUTPUT, T5_ADDRESS) as IDigitalPoint;

				IDigitalPoint i1Point = GetPoint(PointType.DIGITAL_OUTPUT, I1_ADDRESS) as IDigitalPoint;
				IDigitalPoint i2Point = GetPoint(PointType.DIGITAL_OUTPUT, I2_ADDRESS) as IDigitalPoint;

				if (tempCfg == null || tempPoint == null ||
                    t1Point == null || t2Point == null || t3Point == null ||
                    t4Point == null || t5Point == null ||
                    i1Point == null || i2Point == null)
				{
					Thread.Sleep(delayBetweenCommands);
					continue;
				}

				double temperature = tempPoint.EguValue;
				double highAlarm = tempCfg.HighLimit;
				double eguMax = tempCfg.EGU_Max;

				int delta = 0;

				if (t1Point.State == DState.ON)
                {
                    delta -= 2;
                }

				if (t2Point.State == DState.ON)
                {
                    delta -= 2;
                }

				if (t3Point.State == DState.ON)
                {
                    delta -= 2;
                }

				if (t4Point.State == DState.ON)
                {
                    delta -= 5;
                }

				if (t5Point.State == DState.ON)
                {
                    delta -= 3;
                }

				int i1Heating = temperature > 70 ? 6 : 3;
				int i2Heating = temperature > 70 ? 8 : 4;

				if (i1Point.State == DState.ON)
                {
                    delta += i1Heating;
                }

				if (i2Point.State == DState.ON)
                {
                    delta += i2Heating;
                }

				double newTemperature = temperature + delta;

				if (newTemperature < 0)
				{
					newTemperature = 0;
				}

				if (newTemperature > eguMax)
				{
					newTemperature = eguMax;
				}

				ushort raw = eguConverter.ConvertToRaw(
                    tempCfg.ScaleFactor,
                    tempCfg.Deviation,
                    newTemperature);

				processingManager.ExecuteWriteCommand(
                    tempCfg,
                    configuration.GetTransactionId(),
                    configuration.UnitAddress,
                    TEMP_ADDRESS,
                    raw);

				if (newTemperature > highAlarm)
				{
					if (t4Point.State == DState.OFF)
					{
						processingManager.ExecuteWriteCommand(
                            t4Cfg,
                            configuration.GetTransactionId(),
                            configuration.UnitAddress,
                            T4_ADDRESS,
                            1);
					}

					if (t5Point.State == DState.OFF)
					{
						processingManager.ExecuteWriteCommand(
                            t5Cfg,
                            configuration.GetTransactionId(),
                            configuration.UnitAddress,
                            T5_ADDRESS,
                            1);
					}

					if (i1Point.State == DState.ON)
					{
						processingManager.ExecuteWriteCommand(
                            i1Cfg,
                            configuration.GetTransactionId(),
                            configuration.UnitAddress,
                            I1_ADDRESS,
                            0);
					}

					if (i2Point.State == DState.ON)
					{
						processingManager.ExecuteWriteCommand(
                            i2Cfg,
                            configuration.GetTransactionId(),
                            configuration.UnitAddress,
                            I2_ADDRESS,
                            0);
					}
				}

				if (newTemperature >= eguMax)
				{
					if (i1Point.State == DState.ON)
					{
						processingManager.ExecuteWriteCommand(
                            i1Cfg,
                            configuration.GetTransactionId(),
                            configuration.UnitAddress,
                            I1_ADDRESS,
                            0);
					}

					if (i2Point.State == DState.ON)
					{
						processingManager.ExecuteWriteCommand(
                            i2Cfg,
                            configuration.GetTransactionId(),
                            configuration.UnitAddress,
                            I2_ADDRESS,
                            0);
					}
				}

				Thread.Sleep(delayBetweenCommands);
			}
		}

		private IConfigItem GetConfigItem(ushort startAddress)
		{
			foreach (IConfigItem ci in configuration.GetConfigurationItems())
			{
				if (ci.StartAddress == startAddress)
				{
					return ci;
				}
			}

			return null;
		}

		private IPoint GetPoint(PointType type, ushort address)
		{
			List<IPoint> points = storage.GetPoints(
                new List<PointIdentifier>(1)
                {
                    new PointIdentifier(type, address)
                });

			return points.Count > 0 ? points[0] : null;
		}

		#region IDisposable Support

		private bool disposedValue = false;

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
				}

				disposedValue = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}

        public void Start(int delayBetweenCommands)
		{
			this.delayBetweenCommands = delayBetweenCommands * 1000;
            InitializeAndStartThreads();
		}

        public void Stop()
		{
			Dispose();
		}

		#endregion
	}
}