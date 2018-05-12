
#r @"C:\Users\User1\.nuget\packages\newtonsoft.json\11.0.2\lib\net45\Newtonsoft.Json.dll"
#r "System.Net.Http"
#load @"Core.fs"

open System.Diagnostics
open System.Web
open System
open Laika
open Laika.Jupyter
open Newtonsoft.Json.Linq
open Newtonsoft.Json


open Laika.Internal.JupyterApi
let jupyterLocation = @"C:\Users\User1\Anaconda2\envs\tensorflow\Scripts\jupyter.exe"
let jupyterUrl = ""


let startJupyter() =
        let pinfo = ProcessStartInfo()
        pinfo.FileName <- jupyterLocation
        pinfo.Arguments <- "notebook --no-browser"
        pinfo.UseShellExecute <- false
        pinfo.RedirectStandardOutput <- true
        pinfo.RedirectStandardInput <- true
        pinfo.RedirectStandardError <- true
        let p = Process.Start(pinfo)
        let u = 
            Seq.initInfinite (fun _ -> p.StandardError.ReadLine())
            |> Seq.pick
                (fun x ->
                    if x.Contains("The Jupyter Notebook is running at:") then
                        (x.Split ' ') |> Array.last |> Some
                    else  
                        None
                )
            |> Uri
        let q = HttpUtility.ParseQueryString(u.Query)
        let token = 
            if q.AllKeys |> Seq.contains "token" then
                Some q.["token"]
            else    
                None
            
        p, {
            Url = Uri(u.AbsoluteUri.Replace(u.Query, ""))
            Token = token
            Username = None
            Password = None
        }


let p,server = startJupyter()

async {
    Seq.initInfinite (fun _ -> p.StandardError.ReadLine())
    |> Seq.iter (printfn "%s")
} |> Async.Start


let k = Jupyter.kernel "python3" server//{server with Url = Uri("http://localhost:9394")}



let session = 
    {
        Id = ""
        Path = "d9234ue9fjd.ipynb"
        Name = "poo"
        Type = "notebook"
        Kernel = k
    }
let s = Jupyter.session session server

type OptionAsNull<'t>() = 
    inherit JsonConverter()
    override x.CanRead = true
    override x.ReadJson(reader : JsonReader,t,v,s) = 
        printfn "%A" reader.TokenType
        if reader.TokenType = JsonToken.Null then
            box None
        else 
            box (Some(s.Deserialize<'t>(reader)))
    override x.CanWrite = false
    override x.WriteJson(w,v,s) = failwith "cant write"
    override x.CanConvert(t) = t = typeof<Option<'t>>
    
type OptionalString() = 
    inherit JsonConverter()
    override x.CanRead = true
    override x.ReadJson(reader : JsonReader,t,v,s) = 
        if reader.TokenType = JsonToken.Null then
            box ""
        else 
            box (s.Deserialize<'t>(reader))
    override x.CanWrite = false
    override x.WriteJson(w,v,s) = failwith "cant write"
    override x.CanConvert(t) = t = typeof<string>

type FileOrDirContents = 
    | ContentNotRequested
    | FileContent of string
    | DirContent of Contents []

and FileOrDirContentsConverter() = 
    inherit JsonConverter()
    override x.CanRead = true
    override x.ReadJson(reader : JsonReader,t,v,s) = 
        printfn "%A" reader.TokenType
        if reader.TokenType = JsonToken.Null then
            box ContentNotRequested 
        elif reader.TokenType = JsonToken.StartArray then
            s.Deserialize<Contents []>(reader) |> DirContent |> box
        else
            s.Deserialize<string>(reader) |> FileContent |> box
    override x.CanWrite = false
    override x.WriteJson(w,v,s) = failwith "cant write"
    override x.CanConvert(t) = t = typeof<FileOrDirContents>
    

and Contents = 
    {
        [<JsonProperty("name")>]
        /// Name of file or directory, equivalent to the last part of the path
        Name : string
        /// Full path for file or directory
        [<JsonProperty("path")>]
        Path : string
        /// Type of content. directory, file, notebook
        [<JsonProperty("type")>]
        Type : string
        /// indicates whether the requester has permission to edit the file
        [<JsonProperty("writable")>]
        Writable : bool
        /// Creation timestamp
        [<JsonProperty("created")>]
        Created : string
        /// Last modified timestamp
        [<JsonProperty("last_modified")>]
        LastModified : string
        /// The mimetype of a file. If content is not null, and type is 'file’, this will contain the 
        /// mimetype of the file, otherwise this will be null.
        [<JsonProperty("mimetype"); JsonConverter(typeof<OptionalString>)>]
        Mimetype : string
        /// The content, if requested (otherwise null). Will be an array if type is ‘directory’
        [<JsonProperty("content"); JsonConverter(typeof<FileOrDirContentsConverter>)>]
        Content : FileOrDirContents
        /// Format of content (one of '', 'text’, 'base64’, ‘json’)
        [<JsonProperty("format"); JsonConverter(typeof<OptionalString>)>]
        Format : string
    }

let l : Contents = Jupyter.getJson "/api/contents/" [] server




Newtonsoft.Json.Converters.DiscriminatedUnionConverter
let notebook : JObject = Jupyter.putJson "/api/contents" [] (new obj()) server 

notebook.ToString()

type Session() =
    
    [<JsonProperty("id")>]
    member val Id = "" with get,set
    [<JsonProperty("path")>]
    member val Path = "" with get,set
    [<JsonProperty("name")>]
    member val Name = "" with get,set
    [<JsonProperty("type")>]
    member val Type = "" with get,set
    [<JsonProperty("kernel")>]
    member val Kernel = Laika.Internal.JupyterApi.Kernel() with get,set

let sid = Guid.NewGuid()
let session : JObject = Jupyter.putJson "/api/sessions" [] (Session(Id = sid.ToString(), Path = "Untitled.ipynb", Type = "notebook", Kernel = k)) server 

session.ToString()



let sessions : JArray = Jupyter.getJson "/api/sessions" [] server 

session.ToString()

let client = Jupyter.kernelClient k.Id server
Jupyter.executeRequest "a = 1234" 
|> Jupyter.sendMessage client

k.Id