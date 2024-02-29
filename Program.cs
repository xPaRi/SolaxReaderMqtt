using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Solax.SolaxBase;

using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet;

namespace SolaxReaderMqtt
{
    internal class Program
    {
        private const string SOLAX_URI = "SOLAX_URI";
        private const string MQTT_BROKER_URI = "MQTT_BROKER_URI";
        private const string SOLAX_READER_DELAY = "SOLAX_READER_DELAY";

        private static readonly ConsoleColor ConsoleColorDefault = Console.ForegroundColor;
        private static readonly ConsoleColor ConsoleColorError = ConsoleColor.Red;

        static async Task Main(string[] args)
        {
            var delay = 10;

            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            var requestUri = Environment.GetEnvironmentVariable(SOLAX_URI);

            if (requestUri == null)
            {
                Console.WriteLine("Please specific SOLAX_URI environment.");
                return;
            }

            var mqttBrokerUri = Environment.GetEnvironmentVariable(MQTT_BROKER_URI);

            if (mqttBrokerUri == null)
            {
                Console.WriteLine("Please specific MQTT_BROKER_URI environment.");
                return;
            }

            requestUri = @$"http://{requestUri}";

            var delayStr = Environment.GetEnvironmentVariable(SOLAX_READER_DELAY);

            if (delayStr != null)
            {
                if (int.TryParse(delayStr, out var delayTemp))
                {
                    if (delayTemp > 0)
                    {
                        delay = delayTemp;
                    }
                }
            }

            while (true)
            {
                var sw = Stopwatch.StartNew();

                Console.Write($"Get data from Solax Inverter [{requestUri}]");
                var task = GetData(requestUri);

                while (!task.IsCompleted)
                {
                    Console.Write(".");

                    Thread.Sleep(500);
                }

                if (task.Result != null)
                {
                    Console.WriteLine($"\nSending data to Mqtt Broker [{mqttBrokerUri}]... ");

                    await SendData(new SolaxDataSimple(task.Result), mqttBrokerUri);
                }

                sw.Stop();

                if (sw.ElapsedMilliseconds < delay * 1000)
                {
                    Console.WriteLine($"Sleeping [interval is {delay} sec.]... ");
                    Thread.Sleep((int)(delay * 1000 - sw.ElapsedMilliseconds));
                }
            }
        }

        static async Task<SolaxData?> GetData(string requestUri)
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    httpClient.Timeout = new TimeSpan(0, 0, 5);
                    httpClient.DefaultRequestHeaders.Add("X-Forwarded-For", "5.8.8.8");

                    var postData = new StringContent("optType=ReadRealTimeData&pwd=admin");
                    var response = await httpClient.PostAsync(requestUri, postData);
                    var responseData = await response.Content.ReadAsStringAsync();

                    try
                    {
                        return JsonSerializer.Deserialize<SolaxData>(responseData);
                    }
                    catch(Exception ex)
                    {
                        ConsoleWriteLine(ex, true);

                        return null;
                    }
                }
                catch(Exception ex)
                {
                    ConsoleWriteLine(ex, true);

                    return null;
                }
            }
        }

        static async Task SendData(SolaxDataSimple data, string mqttBrokerUri)
        {
            // Vytvoříke 'náklad'
            var payload = JsonSerializer.Serialize(data);

            // Vytvoření instance klienta MQTT
            var factory = new MqttFactory();
            var client = factory.CreateMqttClient();

            // Nastavení parametrů pro připojení k brokeru
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(mqttBrokerUri, 1883) // Adresa a port brokeru
                .WithClientId("SolaxReader") // Unikátní ID klienta
                .WithTimeout(new TimeSpan(0, 0, 3))
                .Build();

            try
            {
                // Připojení klienta k brokeru
                await client.ConnectAsync(options);

                // Vytvoření zprávy
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic("h413/solax") // Název tématu, na které se zpráva odesílá
                    .WithPayload(payload) // Obsah zprávy
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce) // QoS úroveň
                    .WithRetainFlag(false) // Ponechání zprávy na brokeru
                    .Build();

                // Odeslání zprávy
                await client.PublishAsync(message);

                // Odpojení klienta od brokeru
                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                ConsoleWriteLine(ex);
            }
        }

        static void ConsoleWriteLine(Exception ex, bool isExtraLine = false)
        {
            if (isExtraLine) 
            {
                Console.WriteLine();
            }
            Console.ForegroundColor = ConsoleColorError;
            Console.WriteLine(ex.Message);
            Console.ForegroundColor = ConsoleColorDefault;
        }
    }
}