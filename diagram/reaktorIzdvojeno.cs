// ======================================================
// dCom/dCom/RtuCfg.txt
// ZAMENI CEO SADRŽAJ FAJLA OVIM
// ======================================================

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


// ======================================================
// MdbSim/RtuCfg.txt
// ZAMENI CEO SADRŽAJ FAJLA OVIM
// ======================================================

STA 22
TCP 31313

DO_REG  1  5300  0  0    1   0  DO  @Rashlad1
DO_REG  1  5301  0  0    1   0  DO  @Rashlad2
DO_REG  1  5302  0  0    1   0  DO  @Rashlad3
DO_REG  1  5303  0  0    1   0  DO  @GlavniR
DO_REG  1  5304  0  0    1   1  DO  @PomocniR
HR_INT  1  2300  0  0  100  50  AO  @TempJezgra
DO_REG  1  4300  0  0    1   1  DO  @Izvor1
DO_REG  1  4301  0  0    1   0  DO  @Izvor2


// ======================================================
// ConfigItem.cs
// DODAJ POLJA U REGION Fields, ako već ne postoje
// ======================================================

private double scalingFactor;
private double deviation;
private double egu_max;
private double egu_min;
private ushort abnormalValue;
private double highLimit;
private double lowLimit;


// ======================================================
// ConfigItem.cs
// DODAJ PROPERTIES, ako već ne postoje
// ======================================================

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


// ======================================================
// ConfigItem.cs
// DODAJ U KONSTRUKTOR POSLE ČITANJA AcquisitionInterval
// ======================================================

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


// ======================================================
// ConfigItem.cs
// DODAJ OVU METODU U KLASU ConfigItem
// ======================================================

private static bool ParseDouble(string value, out double result)
{
    return Double.TryParse(value, System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out result);
}


// ======================================================
// EGUConverter.cs
// ZAMENI METODU ConvertToEGU OVOM
// ======================================================

public double ConvertToEGU(double scalingFactor, double deviation, ushort rawValue)
{
    return scalingFactor * rawValue + deviation;
}


// ======================================================
// EGUConverter.cs
// DODAJ METODU ConvertToRaw
// ======================================================

public ushort ConvertToRaw(double scalingFactor, double deviation, double eguValue)
{
    if (scalingFactor == 0)
    {
        return (ushort)eguValue;
    }

    return (ushort)((eguValue - deviation) / scalingFactor);
}


// ======================================================
// AlarmProcessor.cs
// ZAMENI METODU GetAlarmForAnalogPoint OVOM
// ======================================================

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


// ======================================================
// AlarmProcessor.cs
// ZAMENI METODU GetAlarmForDigitalPoint OVOM
// ======================================================

public AlarmType GetAlarmForDigitalPoint(ushort state, IConfigItem configItem)
{
    if (state == configItem.AbnormalValue)
    {
        return AlarmType.ABNORMAL_VALUE;
    }

    return AlarmType.NO_ALARM;
}


// ======================================================
// ProcessingManager.cs
// DODAJ POLJA NA POČETAK KLASE
// ======================================================

private AlarmProcessor alarmProcessor;
private EGUConverter eguConverter;


// ======================================================
// ProcessingManager.cs
// DODAJ U KONSTRUKTOR
// ======================================================

this.alarmProcessor = new AlarmProcessor();
this.eguConverter = new EGUConverter();


// ======================================================
// ProcessingManager.cs
// U ProcessDigitalPoint DODAJ OVU LINIJU NA KRAJ METODE
// ======================================================

point.Alarm = alarmProcessor.GetAlarmForDigitalPoint(newValue, point.ConfigItem);


// ======================================================
// ProcessingManager.cs
// U ProcessAnalogPoint DODAJ OVAJ BLOK POSLE Timestamp
// ======================================================

double eguValue = eguConverter.ConvertToEGU(
    point.ConfigItem.ScaleFactor,
    point.ConfigItem.Deviation,
    newValue);

point.EguValue = eguValue;
point.Alarm = alarmProcessor.GetAlarmForAnalogPoint(eguValue, point.ConfigItem);


// ======================================================
// AnalogOutput.cs
// DODAJ using NA VRH FAJLA, ako ga nema
// ======================================================

using ProcessingModule;


// ======================================================
// AnalogOutput.cs
// DODAJ POLJE U KLASU
// ======================================================

private EGUConverter eguConverter = new EGUConverter();


// ======================================================
// AnalogOutput.cs
// U WriteCommand_Execute PRE SLANJA KOMANDE DODAJ RAW KONVERZIJU
// I U ExecuteWriteCommand POŠALJI rawValue UMESTO CommandedValue
// ======================================================

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


// ======================================================
// DigitalOutput.cs
// DODAJ using NA VRH FAJLA
// ======================================================

using System.Collections.Generic;


// ======================================================
// DigitalOutput.cs
// U WriteCommand_Execute DODAJ OVU PROVERU PRE ExecuteWriteCommand
// ======================================================

if ((address == 4300 || address == 4301) && (int)CommandedValue == 1 && !IsTemperatureBelowHighAlarm())
{
    this.stateUpdater.LogMessage("Komanda odbijena: temperatura jezgra nije ispod HighAlarm vrednosti.");
    return;
}


// ======================================================
// DigitalOutput.cs
// DODAJ OVU METODU U KLASU DigitalOutput
// ======================================================

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


// ======================================================
// AutomationManager.cs
// DODAJ using AKO NEDOSTAJE
// ======================================================

using System.Collections.Generic;


// ======================================================
// AutomationManager.cs
// DODAJ POLJE U KLASU
// ======================================================

private IConfiguration configuration;


// ======================================================
// AutomationManager.cs
// DODAJ KONSTANTE U KLASU
// ======================================================

private const ushort TEMP_ADDRESS = 2300;

private const ushort I1_ADDRESS = 4300;
private const ushort I2_ADDRESS = 4301;

private const ushort T1_ADDRESS = 5300;
private const ushort T2_ADDRESS = 5301;
private const ushort T3_ADDRESS = 5302;
private const ushort T4_ADDRESS = 5303;
private const ushort T5_ADDRESS = 5304;

private EGUConverter eguConverter = new EGUConverter();


// ======================================================
// AutomationManager.cs
// U KONSTRUKTOR DODAJ
// ======================================================

this.configuration = configuration;


// ======================================================
// AutomationManager.cs
// ZAMENI TELO METODE AutomationWorker_DoWork OVIM
// ======================================================

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


// ======================================================
// AutomationManager.cs
// DODAJ OVU METODU U KLASU
// ======================================================

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


// ======================================================
// AutomationManager.cs
// DODAJ OVU METODU U KLASU
// ======================================================

private IPoint GetPoint(PointType type, ushort address)
{
    List<IPoint> points = storage.GetPoints(
        new List<PointIdentifier>(1)
        {
            new PointIdentifier(type, address)
        });

    return points.Count > 0 ? points[0] : null;
}
