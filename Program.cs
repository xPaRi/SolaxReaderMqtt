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
    private const string SOLAX_TOPIC = "h413/solax";
    private const string SOLAX_STATUS = "h413/solax/status";

    private static readonly ConsoleColor ConsoleColorDefault = Console.ForegroundColor;
    private static readonly ConsoleColor ConsoleColorError = ConsoleColor.Red;

    private static readonly CancellationTokenSource _cts = new();

    private static readonly HttpClient _httpClient = new() 
    { 
        Timeout = TimeSpan.FromSeconds(5),
        DefaultRequestHeaders = {{ "X-Forwarded-For", "5.8.8.8" }}
    };

    private static IMqttClient? _mqttClient;
    private static MqttClientOptions? _mqttOptions;

    static async Task Main()
    {
        const string SOURCE = "MAIN";

        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        ShowHeader();

        //--- Načtení proměnných z prostředí a kontrola jejich správnosti. Pokud dojde k chybě, isError bude true a program se ukončí.
        var (isError, delay, requestUri, mqttBrokerUri) = GetInitVariables();

        if (isError)
        {
            Console.WriteLine("Please fix the errors and restart the program.");
            return;
        }
        //---

        InitMqtt(mqttBrokerUri, delay);

        if (_mqttClient == null || _mqttOptions == null)
        {
            Console.WriteLine("MQTT client not initialized.");
            return;
        }

        // Reagujeme na Ctrl+C v konzole, abychom mohli korektně ukončit program a odpojit MQTT klienta
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            _cts.Cancel();
        };

        //--- Jedeme v nekonečné smyčce, dokud nedojde k přerušení (Ctrl+C) nebo neočekávané chybě.
        try
        {
            //--- Pošleme, že jsme online
            await _mqttClient.ConnectAsync(_mqttOptions, _cts.Token);
            await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(SOLAX_STATUS).WithPayload("online").WithRetainFlag().Build());
            //---

            var timer = new PeriodicTimer(TimeSpan.FromSeconds(delay));

            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                Console.Write($"Get data from Solax Inverter [{requestUri}]...");
                var data = await GetData(requestUri);

                if (data != null)
                {
                    Console.Write($" OK.\nSending data to Mqtt Broker [{mqttBrokerUri}]... ");
                    await SendData(new SolaxDataSimple(data), mqttBrokerUri);
                }

                Console.WriteLine($"Sleeping {delay} sec... ");
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
            ShowError(SOURCE, ex);
        }
        finally
        {
            // Tady můžeme korektně odpojit MQTT klienta, než se zhasne
            if (_mqttClient.IsConnected == true)
            {
                await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(SOLAX_STATUS).WithPayload("offline").WithRetainFlag().Build());
                
                await _mqttClient.DisconnectAsync();
                Console.WriteLine("MQTT klient odpojen.");
            }
        }
        //---
    }

    static async Task<SolaxData?> GetData(string requestUri)
    {
        const string SOURCE = "GET DATA";

        try
        {
            var postData = new StringContent("optType=ReadRealTimeData&pwd=admin");
            var response = await _httpClient.PostAsync(requestUri, postData, _cts.Token);
            var responseData = await response.Content.ReadAsStringAsync();

            try
            {
                return JsonSerializer.Deserialize<SolaxData>(responseData);
            }
            catch (Exception ex)
            {
                ShowError(SOURCE, ex);
                return null;
            }
        }
        catch (Exception ex)
        {
            ShowError(SOURCE, ex);
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
                await _mqttClient.ConnectAsync(_mqttOptions, _cts.Token);
            }

            // Serializace dat do JSON
            var payload = JsonSerializer.Serialize(solaxData);

            // Vytvoření zprávy
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(SOLAX_TOPIC)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .WithRetainFlag(false)
                .Build();

            // Odeslání zprávy
            await _mqttClient.PublishAsync(message, _cts.Token);

            Console.WriteLine(" OK.");
        }
        catch (Exception ex)
        {
            ShowError("MQTT", ex);
        }
    }

    #region Helpers

    /// <summary>
    /// Zobrazí záhlaví aplkace s názvem a verzí. 
    /// Verze se získá z atributu AssemblyInformationalVersion, 
    /// pokud není k dispozici, zobrazí se "Neznámá verze".
    /// </summary>
    static void ShowHeader()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0000";
        var formattedVersion = version.Insert(version.Length - 2, ":");

        Console.WriteLine($"SolaxReader, ver. {formattedVersion}");
        Console.WriteLine();
    }

    /// <summary>
    /// Vypíše text chyby.
    /// </summary>
    /// <param name="source">Zdroj chyby.</param>
    /// <param name="ex"></param>
    /// <param name="isExtraLine">Napřed odřádkuje.</param>
    static void ShowError(string source, Exception ex)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColorError;
        Console.WriteLine($"{source}: {ex.Message}");
        Console.ForegroundColor = ConsoleColorDefault;
    }

    /// <summary>
    /// Nastaví proměnné podle prostředí a vrátí je v jednom balíčku. 
    /// Pokud dojde k chybě, nastaví isError na true a vypíše chybovou hlášku.
    /// </summary>
    /// <returns></returns>
    static (bool isError, int delay, string requestUri, string mqttBrokerUri) GetInitVariables()
    {
        const string SOLAX_URI = "SOLAX_URI";
        const string MQTT_BROKER_URI = "MQTT_BROKER_URI";
        const string SOLAX_READER_DELAY = "SOLAX_READER_DELAY";

        // Předpokládáme, že nedojde k chybě, dokud se neprokáže opak
        var isError = false;

        //--- SOLAX_READER_DELAY
        var delay = 10;

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

        //--- SOLAX_URI
        var requestUri = Environment.GetEnvironmentVariable(SOLAX_URI);

        if (requestUri == null)
        {
            requestUri = string.Empty;
            isError = true;

            Console.WriteLine("Please specific SOLAX_URI environment.");
        }
        //---

        //--- MQTT_BROKER_URI
        var mqttBrokerUri = Environment.GetEnvironmentVariable(MQTT_BROKER_URI);

        if (mqttBrokerUri == null)
        {
            mqttBrokerUri = string.Empty;
            isError = true;

            Console.WriteLine("Please specific MQTT_BROKER_URI environment.");
        }

        requestUri = @$"http://{requestUri}";
        //---

        return (isError, delay, requestUri, mqttBrokerUri);
    }

    /// <summary>
    /// Inicializace Mqtt klienta. 
    /// Vytvoří instanci MQTT klienta a nastaví možnosti připojení podle zadané URI brokera.
    /// </summary>
    static void InitMqtt(string mqttBrokerUri, int delay)
    {
        var factory = new MqttFactory();

        _mqttClient = factory.CreateMqttClient();

        _mqttOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(mqttBrokerUri, 1883)
            .WithClientId("SolaxReader")
            .WithCleanSession()
            //--- ZÁVĚŤ (LWT) ---
            .WithWillTopic(SOLAX_STATUS)
            .WithWillPayload("offline")
            .WithWillRetain(true)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(2*delay))
            //-------------------
            .Build();
    }

    #endregion
}