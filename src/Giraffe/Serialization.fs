namespace Giraffe.Serialization

// ---------------------------
// JSON
// ---------------------------

[<AutoOpen>]
module Json =
    open System
    open System.IO
    open System.Text
    open System.Text.Json
    open System.Text.Json.Serialization
    open System.Threading.Tasks
    open Newtonsoft.Json
    open Newtonsoft.Json.Serialization
    open FSharp.Control.Tasks.V2.ContextInsensitive
    open Utf8Json

    /// **Description**
    ///
    /// Interface defining JSON serialization methods. Use this interface to customize JSON serialization in Giraffe.
    ///
    [<AllowNullLiteral>]
    type IJsonSerializer =
        abstract member SerializeToString<'T>      : 'T -> string
        abstract member SerializeToBytes<'T>       : 'T -> byte array
        abstract member SerializeToStreamAsync<'T> : 'T -> Stream -> Task

        abstract member Deserialize<'T>      : string -> 'T
        abstract member Deserialize<'T>      : byte[] -> 'T
        abstract member DeserializeAsync<'T> : Stream -> Task<'T>

    /// **Description**
    ///
    /// The `Utf8JsonSerializer` is the default `IJsonSerializer` in Giraffe.
    ///
    /// It uses `Utf8Json` as the underlying JSON serializer to (de-)serialize
    /// JSON content. [Utf8Json](https://github.com/neuecc/Utf8Json) is currently
    /// the fastest JSON serializer for .NET.
    ///
    type Utf8JsonSerializer (resolver : IJsonFormatterResolver) =

        static member DefaultResolver = Utf8Json.Resolvers.StandardResolver.CamelCase

        interface IJsonSerializer with
            member __.SerializeToString (x : 'T) =
                JsonSerializer.ToJsonString (x, resolver)

            member __.SerializeToBytes (x : 'T) =
                JsonSerializer.Serialize (x, resolver)

            member __.SerializeToStreamAsync (x : 'T) (stream : Stream) =
                JsonSerializer.SerializeAsync(stream, x, resolver)

            member __.Deserialize<'T> (json : string) : 'T =
                let bytes = Encoding.UTF8.GetBytes json
                JsonSerializer.Deserialize(bytes, resolver)

            member __.Deserialize<'T> (bytes : byte array) : 'T =
                JsonSerializer.Deserialize(bytes, resolver)

            member __.DeserializeAsync<'T> (stream : Stream) : Task<'T> =
                JsonSerializer.DeserializeAsync(stream, resolver)

    /// **Description**
    ///
    /// TODO
    /// 
    ///
    type SystemTextJsonSerializer (options: JsonSerializerOptions) =

        static member DefaultOptions =
           JsonSerializerOptions(
               PropertyNamingPolicy = JsonNamingPolicy.CamelCase
           )

        interface IJsonSerializer with
            member __.SerializeToString (x : 'T) =
                System.Text.Json.JsonSerializer.Serialize(x,  options)

            member __.SerializeToBytes (x : 'T) =
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(x, options)

            member __.SerializeToStreamAsync (x : 'T) (stream : Stream) =
                System.Text.Json.JsonSerializer.SerializeAsync(stream, x, options)

            member __.Deserialize<'T> (json : string) : 'T =
                System.Text.Json.JsonSerializer.Deserialize<'T>(json, options)

            member __.Deserialize<'T> (bytes : byte array) : 'T =
                System.Text.Json.JsonSerializer.Deserialize<'T>(Span<_>.op_Implicit(bytes.AsSpan()), options)

            member __.DeserializeAsync<'T> (stream : Stream) : Task<'T> =
                System.Text.Json.JsonSerializer.DeserializeAsync<'T>(stream, options).AsTask()

    /// **Description**
    ///
    /// The previous default JSON serializer in Giraffe.
    ///
    /// The `NewtonsoftJsonSerializer` has been replaced by `Utf8JsonSerializer` as
    /// the default `IJsonSerializer` which has much better performance and supports
    /// true chunked transfer encoding.
    ///
    /// The `NewtonsoftJsonSerializer` remains available as an alternative JSON
    /// serializer which can be used to override the `Utf8JsonSerializer` for
    /// backwards compatibility.
    ///
    /// Serializes objects to camel cased JSON code.
    ///
    type NewtonsoftJsonSerializer (settings : JsonSerializerSettings) =
        let serializer = JsonSerializer.Create settings
        let Utf8EncodingWithoutBom = new UTF8Encoding(false)

        static member DefaultSettings =
            JsonSerializerSettings(
                ContractResolver = CamelCasePropertyNamesContractResolver())

        interface IJsonSerializer with
            member __.SerializeToString (x : 'T) =
                JsonConvert.SerializeObject(x, settings)

            member __.SerializeToBytes (x : 'T) =
                JsonConvert.SerializeObject(x, settings)
                |> Encoding.UTF8.GetBytes

            member __.SerializeToStreamAsync (x : 'T) (stream : Stream) = 
                task {
                    use memoryStream = new MemoryStream()
                    use streamWriter = new StreamWriter(memoryStream, Utf8EncodingWithoutBom)
                    use jsonTextWriter = new JsonTextWriter(streamWriter)
                    serializer.Serialize(jsonTextWriter, x)
                    jsonTextWriter.Flush()
                    memoryStream.Seek(0L, SeekOrigin.Begin) |> ignore
                    do! memoryStream.CopyToAsync(stream) 
                } :> Task

            member __.Deserialize<'T> (json : string) =
                JsonConvert.DeserializeObject<'T>(json, settings)

            member __.Deserialize<'T> (bytes : byte array) =
                let json = Encoding.UTF8.GetString bytes
                JsonConvert.DeserializeObject<'T>(json, settings)

            member __.DeserializeAsync<'T> (stream : Stream) = 
                task {
                    use memoryStream = new MemoryStream()         
                    do! stream.CopyToAsync(memoryStream)
                    memoryStream.Seek(0L, SeekOrigin.Begin) |> ignore
                    use streamReader = new StreamReader(memoryStream)
                    use jsonTextReader = new JsonTextReader(streamReader)
                    return serializer.Deserialize<'T>(jsonTextReader)
                }
// ---------------------------
// XML
// ---------------------------

[<AutoOpen>]
module Xml =
    open System.Text
    open System.IO
    open System.Xml
    open System.Xml.Serialization

    /// **Description**
    ///
    /// Interface defining XML serialization methods. Use this interface to customize XML serialization in Giraffe.
    ///
    [<AllowNullLiteral>]
    type IXmlSerializer =
        abstract member Serialize       : obj    -> byte array
        abstract member Deserialize<'T> : string -> 'T

    /// **Description**
    ///
    /// Default XML serializer in Giraffe.
    ///
    /// Serializes objects to UTF8 encoded indented XML code.
    ///
    type DefaultXmlSerializer (settings : XmlWriterSettings) =
        static member DefaultSettings =
            XmlWriterSettings(
                Encoding           = Encoding.UTF8,
                Indent             = true,
                OmitXmlDeclaration = false
            )

        interface IXmlSerializer with
            member __.Serialize (o : obj) =
                use stream = new MemoryStream()
                use writer = XmlWriter.Create(stream, settings)
                let serializer = XmlSerializer(o.GetType())
                serializer.Serialize(writer, o)
                stream.ToArray()

            member __.Deserialize<'T> (xml : string) =
                let serializer = XmlSerializer(typeof<'T>)
                use reader = new StringReader(xml)
                serializer.Deserialize reader :?> 'T