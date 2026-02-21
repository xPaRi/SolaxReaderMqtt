using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Solax.SolaxBase;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace SolaxReaderMqtt;

internal class Program
{
    private const string SOLAX_URI = "SOLAX_URI";
    private const string MQTT_BROKER_URI = "MQTT_BROKER_URI";
    private const string SOLAX_READER_DELAY = "SOLAX_READER_DELAY";

    private static readonly ConsoleColor ConsoleColorDefault = Console.ForegroundColor;
    private static readonly ConsoleColor ConsoleColorError = ConsoleColor.Red;

    private static CancellationTokenSource _cts = new();

    private static readonly HttpClient _httpClient = new() 
    { 
        Timeout = TimeSpan.FromSeconds(5),
        DefaultRequestHeaders = {{ "X-Forwarded-For", "5.8.8.8" }}
    };

    private static IMqttClient? _mqttClient;
    private static MqttClientOptions? _mqttOptions;

    static async Task Main()
    {
        //--- Příprava
        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "Neznámá verze";

        Console.WriteLine($"SolaxReader startuje, verze: {version}");

        var delay = 10;

        Console.CancelKeyPress += (s, e) => {
            e.Cancel = true;
            _cts.Cancel();
        };

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
        //---

        //--- Inicializace MQTT klienta
        var factory = new MqttFactory();

        _mqttClient = factory.CreateMqttClient();

        _mqttOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(mqttBrokerUri, 1883)
            .WithClientId("SolaxReader")
            .WithCleanSession()
            .Build();
        //---

        try
        {
            while (true)
            {
                var sw = Stopwatch.StartNew();

                Console.Write($"Get data from Solax Inverter [{requestUri}]...");
                var data = await GetData(requestUri);

                if (data != null)
                {
                    Console.WriteLine($"\nSending data to Mqtt Broker [{mqttBrokerUri}]... ");
                    await SendData(new SolaxDataSimple(data), mqttBrokerUri);
                }

                sw.Stop();
                var remainingDelay = (delay * 1000) - (int)sw.ElapsedMilliseconds;
                if (remainingDelay > 0)
                {
                    Console.WriteLine($"Sleeping [interval is {delay} sec.]... ");
                    await Task.Delay(remainingDelay, _cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Tady zachytíme Ctrl+C a slušně se rozloučíme
            Console.WriteLine("\nProgram ukončen uživatelem (Ctrl+C).");
        }
        catch (Exception ex)
        {
            // Ostatní nečekané chyby vypíšeme
            ConsoleWriteLine(ex, true);
        }
        finally
        {
            // Tady můžeme korektně odpojit MQTT klienta, než se zhasne
            if (_mqttClient?.IsConnected == true)
            {
                await _mqttClient.DisconnectAsync();
                Console.WriteLine("MQTT klient odpojen.");
            }
        }
    }

    static async Task<SolaxData?> GetData(string requestUri)
    {
        try
        {
            var postData = new StringContent("optType=ReadRealTimeData&pwd=admin");

            // Použití sdílené instance
            var response = await _httpClient.PostAsync(requestUri, postData, _cts.Token);
            var responseData = await response.Content.ReadAsStringAsync();

            try
            {
                return JsonSerializer.Deserialize<SolaxData>(responseData);
            }
            catch (Exception ex)
            {
                ConsoleWriteLine(ex, true);
                return null;
            }
        }
        catch (Exception ex)
        {
            ConsoleWriteLine(ex, true);
            return null;
        }
    }

    static async Task SendData(SolaxDataSimple solaxData, string mqttBrokerUri)
    {
        if (_mqttClient == null || _mqttOptions == null)
        {
            Console.WriteLine("MQTT client not initialized.");
            return;
        }

        try
        {
            // Pokud nejsme připojeni, připojíme se
            if (!_mqttClient.IsConnected)
            {
                // Používáme CancellationToken.None nebo definovaný timeout
                await _mqttClient.ConnectAsync(_mqttOptions, CancellationToken.None);
            }

            // Serializace dat do JSON
            var payload = JsonSerializer.Serialize(solaxData);

            // Vytvoření zprávy
            var message = new MqttApplicationMessageBuilder()
                .WithTopic("h413/solax")
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .WithRetainFlag(false)
                .Build();

            // Odeslání zprávy
            await _mqttClient.PublishAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Pokud se něco nepovede, vypíšeme chybu, ale program běží dál
            Console.ForegroundColor = ConsoleColorError;
            Console.WriteLine($"\nMQTT Error: {ex.Message}");
            Console.ForegroundColor = ConsoleColorDefault;
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