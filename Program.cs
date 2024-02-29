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

            while (true)
            {
                var sw = Stopwatch.StartNew();

                Console.Write($"Get data from Solax Inverter [{requestUri}]");
                var task = GetData(requestUri);

                while (!task.IsCompleted)
                {
                    Console.Write(".");

                    Thread.Sleep(200);
                }

                Console.WriteLine($"\nSending data to Mqtt Broker [{mqttBrokerUri}]... ");

                await SendData(new SolaxDataSimple(task.Result), mqttBrokerUri);

                sw.Stop();

                if (sw.ElapsedMilliseconds < delay * 1000)
                {
                    Console.WriteLine($"Sleeping [{delay} sec.]... ");
                    Thread.Sleep((int)(delay * 1000 - sw.ElapsedMilliseconds));
                }
            }
        }

        static async Task<SolaxData?> GetData(string requestUri)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("X-Forwarded-For", "5.8.8.8");

                var postData = new StringContent("optType=ReadRealTimeData&pwd=admin");
                var response = await httpClient.PostAsync(requestUri, postData);
                var responseData = await response.Content.ReadAsStringAsync();

                return JsonSerializer.Deserialize<SolaxData>(responseData);
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
                .Build();

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


    }
}