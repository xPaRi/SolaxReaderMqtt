using Solax.SolaxBase;
using System.Text.Json.Serialization;

namespace SolaxReaderMqtt;

/// <summary>
/// Tato třída je tu proto, aby nám pomohla s optimalizací serializace a deserializace JSON dat.
/// </summary>
/// <remarks>
/// Pokud to neuděláme, tak při zapnutí optimalizace kompilace (-p:PublishTrimmed=true -p:TrimMode=partial ...) 
/// nebude správně trimována knihovna System.Text.Json, což povede k tomu, že výsledný kód nebude obsahovat 
/// potřebné části pro správnou serializaci a deserializaci a tím pádem bude docházet k chybám při zpracování JSON dat.
/// 
/// Pokud nepoužijeme dekorování třídy pomocí atributů [JsonSourceGenerationOptions] a [JsonSerializable], 
/// tak budeme muset implementovat rozhraní JsonSerializerContext ručním způsobem, což je složitější a náchylnější k chybám.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SolaxData))]
[JsonSerializable(typeof(SolaxDataSimple))]
internal partial class SolaxJsonContext : JsonSerializerContext
{

}