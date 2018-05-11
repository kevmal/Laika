
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
let k = Jupyter.kernel "python3" server//{server with Url = Uri("http://localhost:9394")}



async {
    Seq.initInfinite (fun _ -> p.StandardError.ReadLine())
    |> Seq.iter (printfn "%s")
} |> Async.Start


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