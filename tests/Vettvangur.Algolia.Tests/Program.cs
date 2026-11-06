using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Serialization;
using Vettvangur.Algolia;

// Offline executable regression tests: no database, application startup, or Algolia calls.
var executorType = typeof(IAlgoliaIndexService).Assembly.GetType("Vettvangur.Algolia.AlgoliaIndexExecutor", true)!;
var executor = RuntimeHelpers.GetUninitializedObject(executorType);
var languages = new ILanguage[] { Mock.Of<ILanguage>(language => language.IsoCode == "is-IS"), Mock.Of<ILanguage>(language => language.IsoCode == "en-US") };
#if NET10_0_OR_GREATER
var languageService = new Mock<ILanguageService>();
languageService.Setup(service => service.GetAllAsync())
    .ReturnsAsync(languages);
#else
var languageService = new Mock<ILocalizationService>();
languageService.Setup(service => service.GetAllLanguages()).Returns(languages);
#endif
using var services = new ServiceCollection().AddSingleton(languageService.Object).BuildServiceProvider();
using var cache = new MemoryCache(new MemoryCacheOptions());
SetField("_serviceProvider", services);
SetField("_cache", cache);
var culturesMethod = executorType.GetMethod("GetAllCulturesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
var cultures = await (Task<string[]>)culturesMethod.Invoke(executor, null)!;
Assert(cultures.SequenceEqual(new[] { "is-IS", "en-US" }), "Typed language service returns configured cultures.");
await (Task<string[]>)culturesMethod.Invoke(executor, null)!;
#if NET10_0_OR_GREATER
languageService.Verify(service => service.GetAllAsync(), Times.Once);
#else
languageService.Verify(service => service.GetAllLanguages(), Times.Once);
#endif
Console.WriteLine("PASS: configured cultures are resolved asynchronously and cached.");

#if NET10_0_OR_GREATER
var helper = new DefaultShortStringHelper(new DefaultShortStringHelperConfig());
var nestedPropertyType = new PropertyType(helper, "Umbraco.TextBox", ValueStorageType.Nvarchar, "content") { SupportsPublishing = true };
var blockType = new ContentType(helper, -1) { Key = Guid.NewGuid(), Alias = "textBlock" };
blockType.AddPropertyType(nestedPropertyType);
var contentTypes = new Dictionary<Guid, IContentType> { [blockType.Key] = blockType };
var textFactory = new Mock<IPropertyIndexValueFactory>();
textFactory.Setup(factory => factory.GetIndexValues(
        It.IsAny<IProperty>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(),
        It.IsAny<IEnumerable<string>>(), It.IsAny<IDictionary<Guid, IContentType>>()))
    .Returns((IProperty property, string? culture, string? segment, bool published,
        IEnumerable<string> availableCultures, IDictionary<Guid, IContentType> dictionary) =>
        new[] { new IndexValue { FieldName = property.Alias, Culture = culture, Values = new[] { property.GetValue(culture, segment, published) } } });
var textEditor = new Mock<IDataEditor>();
textEditor.SetupGet(editor => editor.Alias).Returns("Umbraco.TextBox");
textEditor.SetupGet(editor => editor.PropertyIndexValueFactory).Returns(textFactory.Object);
var editors = new List<IDataEditor> { textEditor.Object };
var collection = new PropertyEditorCollection(new DataEditorCollection(() => editors));
var serializer = new SystemTextJsonSerializer(new DefaultJsonSerializerEncoderFactory());
var indexingOptions = new Mock<IOptionsMonitor<IndexingSettings>>();
indexingOptions.SetupGet(options => options.CurrentValue).Returns(new IndexingSettings());
var blockFactoryType = typeof(BlockListPropertyEditor).Assembly.GetType("Umbraco.Cms.Core.PropertyEditors.BlockValuePropertyIndexValueFactory", true)!;
var blockFactory = (IPropertyIndexValueFactory)Activator.CreateInstance(blockFactoryType, collection, serializer, indexingOptions.Object)!;
var blockEditor = new Mock<IDataEditor>();
blockEditor.SetupGet(editor => editor.Alias).Returns("Umbraco.BlockList");
blockEditor.SetupGet(editor => editor.PropertyIndexValueFactory).Returns(blockFactory);
editors.Add(blockEditor.Object);
SetField("_propertyEditorsCollection", collection);

// The actual Umbraco factory mutates shared block metadata when indexing a variant parent.
var variant = MakeProperty("is-IS", "Variant text");
blockFactory.GetIndexValues(variant, "is-IS", null, true, cultures, contentTypes).ToList();
Assert(nestedPropertyType.Variations == ContentVariation.Culture, "Fixture reproduces metadata promotion.");
var invariant = MakeProperty(null, "Invariant text");
try
{
    blockFactory.GetIndexValues(invariant, null, null, true, Array.Empty<string>(), contentTypes).ToList();
    throw new InvalidOperationException("Legacy empty-culture case should reproduce the crash.");
}
catch (NullReferenceException)
{
    Console.WriteLine("PASS: legacy empty-culture case reproduces the real Umbraco block extraction crash.");
}

var readMethod = executorType.GetMethod("ReadPropertyValue", BindingFlags.NonPublic | BindingFlags.Instance)!;
var value = readMethod.Invoke(executor, new object?[] { invariant, null, cultures, contentTypes });
Assert(value?.ToString()?.Contains("Invariant text", StringComparison.Ordinal) == true, "Invariant text survives variant-first indexing.");
Console.WriteLine("PASS: corrected cultures preserve invariant block text after variant indexing.");

// Verify reverse processing order as well.
nestedPropertyType.Variations = ContentVariation.Nothing;
value = readMethod.Invoke(executor, new object?[] { invariant, null, cultures, contentTypes });
Assert(value?.ToString()?.Contains("Invariant text", StringComparison.Ordinal) == true, "Invariant-first text.");
value = readMethod.Invoke(executor, new object?[] { variant, "is-IS", cultures, contentTypes });
Assert(value?.ToString()?.Contains("Variant text", StringComparison.Ordinal) == true, "Variant text after invariant indexing.");
Console.WriteLine("PASS: invariant-first and variant-second block extraction retain their text.");

Property MakeProperty(string? culture, string text)
{
    var key = Guid.NewGuid();
    var json = System.Text.Json.JsonSerializer.Serialize(new
    {
        layout = new Dictionary<string, object>(),
        contentData = new[] { new { contentTypeKey = blockType.Key, key, values = new[] { new { editorAlias = "Umbraco.TextBox", alias = "content", culture = (string?)null, segment = (string?)null, value = text } } } },
        settingsData = Array.Empty<object>(),
        expose = new[] { new { contentKey = key, culture, segment = (string?)null } }
    });
    var propertyType = new PropertyType(helper, "Umbraco.BlockList", ValueStorageType.Ntext, "content2")
    {
        SupportsPublishing = true,
        Variations = culture is null ? ContentVariation.Nothing : ContentVariation.Culture
    };
    var property = new Property(propertyType);
    property.SetValue(json, culture);
    property.PublishValues(culture ?? "*");
    return property;
}
#endif

void SetField(string name, object value)
{
    executorType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(executor, value);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
