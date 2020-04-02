using CsvHelper;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using Windows.ApplicationModel.Background;
using Windows.Devices.Gpio;
using Windows.System.Threading;

namespace CovidAlertsIoT
{
    public sealed class StartupTask : IBackgroundTask
    {
        private BackgroundTaskDeferral deferral;
        private GpioPinValue value = GpioPinValue.High;

        private GpioPin redPin;
        private GpioPin yellowPin;
        private GpioPin greenPin;
        private ThreadPoolTimer dataTimer;
        private ThreadPoolTimer lightsTimer;
        private bool isCovidAlertState = false;
        private bool isCovidAlertCounty = false;

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            DotNetEnv.Env.Load();
            deferral = taskInstance.GetDeferral();
            InitGPIO();
            dataTimer = ThreadPoolTimer.CreatePeriodicTimer(Data_Timer_Tick, TimeSpan.FromHours(Constants.DATA_TIMER_HOURS));
            lightsTimer = ThreadPoolTimer.CreatePeriodicTimer(Lights_Timer_Tick, TimeSpan.FromMilliseconds(Constants.LIGHTS_TIMER_MILLISECONDS));
        }

        private void InitGPIO()
        {
            redPin = GpioController.GetDefault().OpenPin(Constants.RED_LED_PIN);
            redPin.Write(GpioPinValue.High);
            redPin.SetDriveMode(GpioPinDriveMode.Output);

            yellowPin = GpioController.GetDefault().OpenPin(Constants.YELLOW_LED_PIN);
            yellowPin.Write(GpioPinValue.High);
            yellowPin.SetDriveMode(GpioPinDriveMode.Output);

            greenPin = GpioController.GetDefault().OpenPin(Constants.GREEN_LED_PIN);
            greenPin.Write(GpioPinValue.High);
            greenPin.SetDriveMode(GpioPinDriveMode.Output);
        }

        private void Data_Timer_Tick(ThreadPoolTimer timer)
        {
            GetCovidStatus();
        }

        private void Lights_Timer_Tick(ThreadPoolTimer timer)
        {
            RunLights();
        }

        private async void GetCovidStatus()
        {
            string county = Environment.GetEnvironmentVariable("COUNTY", EnvironmentVariableTarget.Process);
            string state = Environment.GetEnvironmentVariable("STATE", EnvironmentVariableTarget.Process);
            using (var client = new HttpClient())
            {
                string today;
                string csvData;
                try
                {
                    today = DateTime.Today.ToString("MM-dd-yyyy");
                    csvData = await client.GetStringAsync($"https://raw.githubusercontent.com/CSSEGISandData/COVID-19/master/csse_covid_19_data/csse_covid_19_daily_reports/{today}.csv");
                }
                catch (Exception)
                {
                    // data for today is not available, get data from yesterday
                    today = DateTime.Today.AddDays(-1).ToString("MM-dd-yyyy");
                    csvData = await client.GetStringAsync($"https://raw.githubusercontent.com/CSSEGISandData/COVID-19/master/csse_covid_19_data/csse_covid_19_daily_reports/{today}.csv");
                }
                using (var csv = new CsvReader(new StringReader(csvData), CultureInfo.InvariantCulture))
                {
                    var records = csv.GetRecords<CovidData>();
                    int countyCases = records.Where(record => record.Combined_Key == county).Select((selector) => selector.Confirmed).First();
                    int stateCases = records.Where(record => record.Province_State == state).Sum(selector => selector.Confirmed);
                    isCovidAlertCounty = countyCases > Constants.COUNTY_THRESHOLD;
                    isCovidAlertState = stateCases > Constants.STATE_THRESHOLD;
                }
            }
        }

        private void RunLights()
        {
            value = (value == GpioPinValue.High) ? GpioPinValue.Low : GpioPinValue.High;

            if (!isCovidAlertCounty && !isCovidAlertState)
            {
                greenPin.Write(value);
                redPin.Write(GpioPinValue.High);
                yellowPin.Write(GpioPinValue.High);
            }
            if (isCovidAlertState)
            {
                yellowPin.Write(value);
                greenPin.Write(GpioPinValue.High);
            }
            if (isCovidAlertCounty)
            {
                redPin.Write(value);
                greenPin.Write(GpioPinValue.High);
            }
        }
    }
}