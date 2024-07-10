# MultiProcess Named Pipe Server-Client Communication in C# (.NET)

## Build the Project

```bash
dotnet build InterProcessCommunication.sln
```

## Usage

Server Pipe
```bash
NamedPipeServer server = new NamedPipeServer();
server.StartListening();

#On Closing
server.StopNotify()
```


Client Pipe
```bash
var client = new NamedPipeClient();
client.StartListening(); 

//On Closing
client.StopNotify()
```

