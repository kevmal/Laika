namespace Laika
open System.Net.WebSockets
open System.Threading
open System
open Newtonsoft.Json
open System
open System.Collections.Generic
open System.Text
open System.Runtime.Serialization
open Newtonsoft.Json.Linq

module Internal = 
    module JupyterApi = 
        type KernelSpecResources() =
            /// path for kernel.js file
            [<JsonProperty("kernel.js")>]
            member val KernelJs : string = "" with get,set
            /// path for kernel.css file
            [<JsonProperty("kernel.css")>]
            member val KernelCss : string = "" with get,set
            /// path for logo file.  Logo filenames are of the form `logo-widthxheight`
            [<JsonProperty("logo-*")>]
            member val Logo : string = "" with get,set
        type KernelSpecFileHelpLinksItem() = 
            /// menu item link text
            [<JsonProperty("text")>]
            member val Text : string = "" with get,set
            /// menu item link url
            [<JsonProperty("url")>]
            member val Url : string = "" with get,set
        type KernelSpecFile() =
            /// The programming language which this kernel runs. This will be stored in notebook metadata.
            [<JsonProperty("language")>]
            member val Language : string = "" with get,set
            /// A list of command line arguments used to start the kernel.
            /// The text `{connection_file}` in any argument will be replaced with the path to the connection file.
            [<JsonProperty("argv")>]
            member val Argv : string[] = Array.empty with get,set
            /// The kernel's name as it should be displayed in the UI. Unlike the kernel name used in the API, this can contain arbitrary unicode characters.
            [<JsonProperty("display_name")>]
            member val DisplayName : string = "" with get,set
            /// Codemirror mode.  Can be a string *or* an valid Codemirror mode object.  This defaults to the string from the `language` property.
            [<JsonProperty("codemirror_mode")>]
            member val CodemirrorMode : string = ""  with get,set
            /// A dictionary of environment variables to set for the kernel. These will be added to the current environment variables.
            [<JsonProperty("env")>]
            member val Env : IDictionary<string, string> = Dictionary<_,_>() :> _ with get,set
            /// Help items to be displayed in the help menu in the notebook UI.
            [<JsonProperty("help_links")>]
            member val HelpLinks : KernelSpecFileHelpLinksItem [] = Array.empty with get,set
        type KernelSpec() =
            /// Unique name for kernel
            [<JsonProperty("name")>]
            member val Name : string = "" with get,set
            /// Kernel spec json file
            [<JsonProperty("KernelSpecFile")>]
            member val KernelSpecFile : KernelSpecFile = KernelSpecFile() with get,set
            [<JsonProperty("resources")>]
            member val Resources : KernelSpecResources = KernelSpecResources() with get,set
        type KernelspecsResponse() = 
            /// The name of the default kernel.
            [<JsonProperty("default")>]
            member val Default : string = "" with get,set
            [<JsonProperty("kernelspecs")>]
            member val Kernelspecs : IDictionary<string, KernelSpec> = Dictionary<_,_>() :> _ with get,set
        type Kernel() = 
            /// uuid of kernel
            [<JsonProperty("id")>]
            member val Id : string = "" with get,set
            [<JsonProperty("name")>]
            member val Name : string = "" with get,set
            /// ISO 8601 timestamp for the last-seen activity on this kernel.
            /// Use this in combination with execution_state == 'idle' to identify
            /// which kernels have been idle since a given time.
            /// Timestamps will be UTC, indicated 'Z' suffix.
            /// Added in notebook server 5.0.
            [<JsonProperty("last_activity")>]
            member val LastActivity : string = "" with get,set
            /// The number of active connections to this kernel.
            [<JsonProperty("connections")>]
            member val Connections : int = 0 with get,set
            /// Current execution state of the kernel (typically 'idle' or 'busy', but may be other values, such as 'starting').
            /// Added in notebook server 5.0.
            [<JsonProperty("execution_state")>]
            member val ExecutionState : string = "" with get,set
            override x.ToString() = 
                sprintf "%s_%s_%s" x.Name x.Id x.LastActivity

        type KernelName() =
            [<JsonProperty("name")>]
            member val Name : string = "" with get,set
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
            member val Kernel = Kernel() with get,set
            

module Jupyter = 
    open Internal.JupyterApi
    open System.Web
    open System.Threading.Tasks
    open System.Net.Http
    open System.Net
    open System.Net.Http.Headers
    

    let jsonSettings = JsonSerializerSettings()
    //jsonSettings.NullValueHandling <- NullValueHandling.Ignore
    let serialize obj = JsonConvert.SerializeObject(obj, jsonSettings)

    type Header = 
        {
            [<JsonProperty("msg_id")>]
            MsgId : string
            [<JsonProperty("username")>]
            Username : string
            [<JsonProperty("session")>]
            Session : string
            [<JsonProperty("date")>]
            Date : DateTime
            [<JsonProperty("msg_type")>]
            MsgType : string
            [<JsonProperty("version")>]
            Version : string
        }


    type ParentHeaderConverter() = 
        inherit JsonConverter()
        override __.CanRead = false
        override __.CanWrite = true
        override __.CanConvert(t) = 
            typeof<Header option> = t
        override __.ReadJson(r,o,ev,s) = failwith "no"
        override __.WriteJson(w,v,s) = 
            match v :?> Header option with
            | Some x -> s.Serialize(w,x)
            | None -> s.Serialize(w,dict [])
            
    //jsonSettings.Converters.Add (ParentHeaderConverter())

    let header msgType = 
        {
            MsgId = Guid.NewGuid().ToString()
            Username = ""
            Session = ""
            Date = DateTime.Now
            MsgType = msgType
            Version = "5.0"
        }

    //JsonPropertyAttribute("").DefaultValueHandling
    type IMessageContent = 
        abstract member MessageType : string

    type Message = 
        {
            [<JsonProperty("metadata")>]
            Metadata : IDictionary<string,string>
            [<JsonProperty("channel")>]
            Channel : string
            [<JsonProperty("header")>]
            Header : Header
            [<JsonProperty("parent_header"); JsonConverter(typeof<ParentHeaderConverter>)>]
            ParentHeader : Header option
            [<JsonProperty("content")>]
            Content : IMessageContent
            [<JsonProperty("buffers")>]
            Buffers : byte [] list
        }
        
    type RawMessageContent(o : JObject) = 
        override x.ToString() = o.ToString()
        interface IMessageContent with
            member x.MessageType = ""
        
    let msgTypes = Dictionary<string,Type>()
    type MessageConverter() = 
        inherit JsonConverter() 
        override __.CanRead = true
        override __.CanWrite = false
        override __.CanConvert(t) = typeof<Message> = t
        override __.ReadJson(r,o,ev,s) = 
            let jo = s.Deserialize<JObject>(r)
            let scc,v = msgTypes.TryGetValue(jo.["msg_type"].ToObject())
            let v = 
                if scc then
                    jo.["content"].ToObject(v) :?> IMessageContent
                else
                    RawMessageContent(jo) :> _
            let parentHeader = 
                let scc,v = jo.TryGetValue("parent_header")
                if scc then 
                    if v.HasValues then
                        Some (jo.["parent_header"].ToObject())
                    else
                        None
                else
                    None
            {
                Metadata = jo.["metadata"].ToObject()
                Channel = jo.["channel"].ToObject()
                Header = jo.["header"].ToObject()
                ParentHeader = parentHeader
                Content = v
                Buffers = jo.["buffers"].ToObject()
            } :> obj
        override __.WriteJson(w,v,s) = 
            let v = v :?> Message
            let jobj = JObject()
            jobj.Add("metadata",JToken.FromObject(v.Metadata))
            jobj.Add("channel",JToken.FromObject(v.Channel))
            jobj.Add("header",JToken.FromObject(v.Header))
            match v.ParentHeader with
            | Some h -> jobj.Add("parent_header",JToken.FromObject(h))
            | None -> jobj.Add("parent_header",JToken.FromObject(dict []))
            jobj.Add("content",JToken.FromObject(v.Content))
            if List.isEmpty v.Buffers then
                jobj.Add("buffers",JToken.FromObject(dict []))
            else
                jobj.Add("buffers",JToken.FromObject(v.Buffers))
            s.Serialize(w,jobj)

    jsonSettings.Converters.Add (MessageConverter())

    type ExecuteRequestContent = 
        {
            [<JsonProperty("code")>]
            Code : string
            [<JsonProperty("silent")>]
            Silent : bool
            [<JsonProperty("store_history")>]
            StoreHistory : bool
            [<JsonProperty("user_expressions")>]
            UserExpressions : IDictionary<string,string>
            [<JsonProperty("allow_stdin")>]
            AllowStdin : bool
            [<JsonProperty("stop_on_error")>]
            StopOnError : bool
        }
        interface IMessageContent with
            member x.MessageType = "execute_request"



    type Payload = 
        | Page of Data : string * Start : int
        | SetNextInput of Text : string * Replace : bool
        | Edit of Filename : string * LineNumber : int
        | AskExit of KeepKernel : bool

    type ExecuteReply = 
        {
            [<JsonProperty("status")>]
            Status : string
            [<JsonProperty("execution_count")>]
            ExecutionCount : int
            [<JsonProperty("payload")>]
            Payload : IDictionary<string,string> list
            [<JsonProperty("user_expressions")>]
            UserExpressions : IDictionary<string,string>
        }
        interface IMessageContent with
            member x.MessageType = "execute_reply"
            

    type InspectRequest =
        {
            Code : string
            CursorPosition : int
            DetailLevel : int
        }

    type InspectReply = 
        {
            Status : string
            Found : bool
            Data : IDictionary<string,string>
            Metadata : IDictionary<string,string>
        }

    type CompleteRequest = 
        {
            Code : string
            CursorPosition : int
        }

    type CompleteReply =
        {
            Matches : string list
            CursorStart : int
            CursorEnd : int
            Metadata : IDictionary<string,string>
            Status : string
        }
    type HistoryAccessType = Range | Tail | Search
    type HistoryRequest = 
        {
            Output : bool
            Raw : bool
            HistoryAccessType : HistoryAccessType
            Session : int
            Start : int 
            Stop : int
            N : int
            Pattern : string
            Unique : bool
        }
    type HistoryEntry = 
        {
            Session : string
            LineNumber : int
            Input : string
            Output : string option
        }
    type HistoryReply = 
        {
            History : HistoryEntry list
        }

    type IsCompleteRequest = 
        {
            Code : string
        }

    type CompleteStatus = Complete | Incomplete | Invalid | Unknown
    type IsCompleteReply = 
        {
            Status : CompleteStatus
            Indent : string
        }

    type CommInfoRequest = 
        {
            TargetName : string
        }

    type CommInfoReply = 
        {
            Comms : IDictionary<string, string>
        }

    type CodemirrorMode = 
        {
            String : string
            Dict : IDictionary<string,string>
        }
    type LanguageInfo = 
        {
            Name : string
            Mimetype : string
            FileExtension : string
            PygmentsLexer : string
            CodemirrorMode : CodemirrorMode
            NbconverExporter : string
        }
    type HelpLink = 
        {
            Text : string
            Url : string
        }
    type KernelInfoReply = 
        {
            ProtocolVersion : string
            Implementation : string
            ImplementationVersion : string
            LanguageInfo : LanguageInfo
            Banner : string
            HelpLinks : HelpLink list
        }

    type ShutdownRequest = 
        {
            Restart : bool
        }

    type Stream = 
        {
            [<JsonProperty("name")>]
            Name : string
            [<JsonProperty("text")>]
            Text : string
        }
        interface IMessageContent with  
            member x.MessageType = "stream"
        
    type DisplayData = 
        {
            [<JsonProperty("data")>]
            Data : IDictionary<string,string>
            [<JsonProperty("metadata")>]
            Metadata : IDictionary<string,string>
            [<JsonProperty("transient")>]
            Transient : IDictionary<string,string>
        }
        interface IMessageContent with  
            member x.MessageType = "display_data"

    type ExecuteInput = 
        {
            Code : string
            ExecutionCount : int
        }

    type ExecuteResult = 
        {
            [<JsonProperty("data")>]
            Data : IDictionary<string,string>
            [<JsonProperty("metadata")>]
            Metadata : IDictionary<string,string>
            [<JsonProperty("execution_count")>]
            ExecutionCount : int
        }
        interface IMessageContent with  
            member x.MessageType = "execute_result"

    type Error = 
        {
            [<JsonProperty("execution_count")>]
            ExecutionCount : int
            [<JsonProperty("payload")>]
            Payload : IDictionary<string,string> list
            [<JsonProperty("user_expressions")>]
            UserExpressions : IDictionary<string,string>
        }
        interface IMessageContent with  
            member x.MessageType = "error"

    type ExecutionStatus = Busy | Idle | Starting

    type KernelStatus = 
        {
            ExecutionStatus : ExecutionStatus
        }

    type InputRequest =     
        {
            Prompt : string
            Password : bool
        }

    type InputReply = 
        {
            Value : string
        }

    msgTypes.["execute_request"] <- typeof<ExecuteRequestContent>
    msgTypes.["execute_reply"] <- typeof<ExecuteReply>
    msgTypes.["execute_result"] <- typeof<ExecuteResult>
    msgTypes.["error"] <- typeof<Error>
    msgTypes.["stream"] <- typeof<Stream>
    msgTypes.["display_data"] <- typeof<DisplayData>


    let message channel (content : IMessageContent) = 
        {
            Header = header content.MessageType
            Content = content
            Metadata = dict []
            ParentHeader = None
            Buffers = []
            Channel = channel
        }


    let executeRequest code = 
        message "shell" {
                Code = code
                Silent = false
                StoreHistory = false
                UserExpressions = dict []
                AllowStdin = true
                StopOnError = false
        }

    type ConnectionMessage = 
        //| Send of Message
        | RecvText of string
        | WebSocketClose
        | Listen of (bool ref*IObserver<Message>)

    type WsMessage = 
        | Message of Message
        | ParseError of Exception*string
        | Close

    type KernelConnection = 
        {
            WebSocket : ClientWebSocket
            Agent : MailboxProcessor<ConnectionMessage>
            Inbox : MailboxProcessor<WsMessage>
        }
        
    type Session = 
        {
            Connection : KernelConnection
            SessionId : Guid
        }
        member x.Bind(v,f) = 
            f 

    let (|ExecuteReply|_|) (x : Message) = 
        match x with
        | ({Content = :? ExecuteReply as x}) -> Some x
        | _ -> None
    let (|ExecuteResult|_|) (x : Message) = 
        match x with
        | ({Content = :? ExecuteResult as x}) -> Some x
        | _ -> None
        
    let (|Error|_|) (x : Message) = 
        match x with
        | ({Content = :? Error as x}) -> Some x
        | _ -> None

    let (|MsgId|_|) mid (x : Message) = 
        match x with
        | {ParentHeader = Some {MsgId = id}} when id = mid -> Some()
        | _ -> None


    let processor state (msg : WsMessage) =
        match msg with
        | ParseError _ -> state
        | Close -> state
        | Message m -> 
            state |> List.filter (fun x -> x m)


    type Server = 
        {
            Token : string option 
            Username : string option
            Password : string option
            Url : Uri
        }

    let server url token = 
        {
            Token = Some token
            Username = None
            Password = None
            Url = Uri(url)
        }

    let internal buildUrl (url : Uri) parameters =
        let u = UriBuilder(url)
        let q = HttpUtility.ParseQueryString(u.Query)
        for name,value in parameters do
            q.[name] <- value
        u.Query <- q.ToString()
        u.Uri
        
    let internal loc (name : string) parameters (server : Server) = 
        match server.Token with
        | Some t -> buildUrl (Uri(server.Url, name)) (("token", t) :: parameters)
        | None -> buildUrl (Uri(server.Url, name)) (parameters)

    exception ExpectingJsonResponse of name : string*reponse:string
    let getJsonString name parameters server =
        use client = new HttpClient()
        let response = client.GetAsync(loc name parameters server).Result
        if response.Content.Headers.ContentType.MediaType = "application/json" then 
            response.Content.ReadAsStringAsync().Result
        else
            raise (ExpectingJsonResponse(name,response.Content.Headers.ContentType.MediaType))

    let getJson name parameters server : 'a = 
        let str = (getJsonString name parameters server) 
        printfn "%s" str
        JsonConvert.DeserializeObject<'a> str

    let putJson name parameters o server : 'a = 
        let payload = Newtonsoft.Json.JsonConvert.SerializeObject o
        use client = new HttpClient()
        use content = new StringContent(payload)
        content.Headers.ContentType <- MediaTypeHeaderValue("application/json")
        let url = loc name parameters server
        let response = client.PostAsync(url,content).Result
        let textResponse = response.Content.ReadAsStringAsync().Result
        JsonConvert.DeserializeObject<'a>(textResponse)
        
    let delete name parameters server = 
        use client = new HttpClient()
        let url = loc name parameters server
        client.DeleteAsync(url).Result
        
    
    let getJsonObj name parameters server = 
        Newtonsoft.Json.Linq.JObject.Parse(getJsonString name parameters server)
    
    let kernelSpecs (server : Server) : KernelspecsResponse = getJson "/api/kernelspecs" [] server
    let kernel name (server : Server) : Kernel = 
        let o = KernelName(Name = name)
        putJson "/api/kernels" [] o server 
    let kernelInfo kernelId (server : Server) : Kernel = 
        getJson (sprintf "/api/kernels/%s" kernelId) [] server 
   
    let kernelDelete kernelId (server : Server) = 
        delete (sprintf "/api/kernels/%s" kernelId) [] server
        

    let listKernels (server : Server) : Kernel list = 
        getJson "/api/kernels" [] server 

    let websocketClient kernelId server = 
        let u = UriBuilder(loc (sprintf "/api/kernels/%s/channels" kernelId) [] server)
        u.Scheme <- "ws"
        async {
            let w = new WebSockets.ClientWebSocket()
            let! cc = Async.CancellationToken
            do! w.ConnectAsync(u.Uri, cc) |> Async.AwaitTask
            return w
        }
        
    type IncomingMessages(agent : MailboxProcessor<ConnectionMessage> ) = 
        interface IObservable<Message> with
            member x.Subscribe(observer : IObserver<_>) = 
                let active = ref true
                agent.Post(Listen (active, observer))
                {new IDisposable with
                    member x.Dispose() = active := false}

    type KernelClient =     
        {
            KernelId : string
            WebsocketClient : ClientWebSocket
            SendQueue : MailboxProcessor<string*CancellationToken>
            Reader : Task<unit>
            Incoming : IncomingMessages
        }

    let sendAgent (c : ClientWebSocket) =   
        MailboxProcessor.Start
            (fun inbox ->
                let rec loop () = 
                    async{
                        let! (msg : string,cc : CancellationToken) = inbox.Receive()
                        if not cc.IsCancellationRequested then
                            do! c.SendAsync(ArraySegment(Text.Encoding.UTF8.GetBytes(msg)),WebSocketMessageType.Text,true,cc) |> Async.AwaitTask
                            return! loop ()
                        else
                            return! loop ()
                    }
                loop()
            )


    let reader inBufferLength (ws : ClientWebSocket) = 
        let inbox =  
            MailboxProcessor.Start
                (fun inbox ->
                    let rec loop (listeners : (bool ref * IObserver<Message>) list) = 
                        async{
                            let! (msg : ConnectionMessage) = inbox.Receive()
                            match msg with 
                            | RecvText msg -> 
                                try 
                                    let m = JsonConvert.DeserializeObject<Message>(msg,jsonSettings)
                                    let ls = 
                                        listeners 
                                        |> List.filter
                                            (fun (active,o) ->
                                                try
                                                    if !active then
                                                        o.OnNext(m)
                                                        true
                                                    else
                                                        false
                                                with
                                                | ex -> 
                                                    //TODO : log error at least
                                                    o.OnError(ex) //REVIEW: is this the right thing?
                                                    true
                                            )
                                    return! loop ls
                                with
                                | e -> 
                                    //TODO : Get rid of the printing add logging
                                    printfn "MESSAGE EX:"
                                    printfn "%s" e.Message
                                    printfn "%s" msg 
                                    //REVIEW: what to do here, use OnError, or maybe another observable?
                                    return! loop listeners
                            | WebSocketClose -> 
                                //REVIEW: what to do, try to recover? OnComplete?
                                listeners |> List.iter (fun (active,o) -> if !active then o.OnCompleted())
                            | Listen(active,o) -> 
                                return! loop ((active,o) :: listeners)
                        }
                    loop []
                )
        let buffer = Array.zeroCreate inBufferLength
        let bufferSegment = ArraySegment(buffer)
        let rec readStart() = 
            async {
                let! ctoken = Async.CancellationToken 
                let! recv = ws.ReceiveAsync(bufferSegment,ctoken) |> Async.AwaitTask
                match recv.EndOfMessage,recv.MessageType with
                | true, WebSocketMessageType.Text -> 
                    inbox.Post(RecvText(Encoding.UTF8.GetString(buffer,0,recv.Count)))
                    return! readStart()
                | false, WebSocketMessageType.Text -> 
                    let sb = StringBuilder()
                    sb.Append(Encoding.UTF8.GetString(buffer,0,recv.Count)) |> ignore
                    return! readText sb
                | _, WebSocketMessageType.Close -> inbox.Post WebSocketClose
                | _, WebSocketMessageType.Binary -> 
                    //printfn "Binary data?"
                    //TODO: log a warning
                    return! readStart()
                | _, msgType -> 
                    printfn "Unexpected %A" msgType
                    failwithf "Unexpected %A" msgType
            }
        and readText (sb : StringBuilder) = 
            async {
                let! ctoken = Async.CancellationToken 
                let! recv = ws.ReceiveAsync(bufferSegment,ctoken) |> Async.AwaitTask
                match recv.EndOfMessage,recv.MessageType with
                | eom, WebSocketMessageType.Text -> 
                    sb.Append(Encoding.UTF8.GetString(buffer,0,recv.Count)) |> ignore
                    if eom then
                        inbox.Post(RecvText(sb.ToString()))
                    else
                        return! readText sb
                | _, WebSocketMessageType.Close -> inbox.Post WebSocketClose
                | _, msgType -> 
                    printfn "Unexpected %A" msgType
                    failwithf "Unexpected %A" msgType
            }
        readStart() |> Async.StartAsTask,
            IncomingMessages(inbox)
        
    let kernelClient kernelId server = 
        let wc = websocketClient kernelId server |> Async.RunSynchronously
        let reader,incoming = reader (16*1024) wc
        {
            KernelId = kernelId
            WebsocketClient = wc
            SendQueue = sendAgent wc
            Reader = reader
            Incoming = incoming
        }
        
    let sendMessage (client : KernelClient) (msg : Message) = client.SendQueue.Post((serialize msg,Async.DefaultCancellationToken))
        
    let sendMessageObservable (client : KernelClient) (msg : Message) = 
        let msgId = msg.Header.MsgId
        { new IObservable<Message> with
            member x.Subscribe(o : IObserver<Message>) = 
                let d = 
                    let observable = 
                        client.Incoming 
                        |> Observable.filter (function {ParentHeader = Some {MsgId = mid}} when mid = msgId -> true | _ -> false)
                    observable.Subscribe(o)
                client.SendQueue.Post((serialize msg,Async.DefaultCancellationToken))
                d
        }
