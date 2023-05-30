# Mirror FileTransfer
This is a rather simple solution that arouse from a need to send large files between the server and clients. I didn't want to host files and download them with HTTP requests when we already have a direct connection. This plugin will let you send a large chunk of data directly to a client or server by utilising the connection setup already by the Mirror plugin.
 
## Dependencies
If you're using Mirror for Unity as your Networking solution this plugin should work out of the box. There are no dependencies on anything except for Mirror itself.

## How to use it
The namespace is `Gameclaw` and the main class is `FileTransfer`. Everything has comments so should be relatively easy to decipher. Nonetheless here is a simple example and explanation to get you started. First of all, you can setup some delegates that will get invoked whenever a file transfer begins, ends, fails. They are the following:
```csharp
        public static Action<FileTransferProgress> OnBeginRecievingFile;
        public static Action<object, string> OnRecieveObject;
        public static Action<Stream, string> OnRecieveStream;
        public static Action<string> OnFailedToRecieveFile;
```
When the server or client gets it's first chunk of data for a new file transfer it will invoke `OnBeginRecievingFile`. It also gives you a `FileTransferProgress` object that you can poll to see the progress of the transfer.
Once the transfer is complete, it will either invoke `OnRecieveObject` or `OnRecieveStream` depending on which method overload for `SendData...` you decide to use.
Here's an example for creating a method to listen for receiving files:
```csharp
using Gameclaw;

void Start()
{
    // Listen for when we receive a file
    FileTransfer.OnRecieveStream = ReceivedStream;
    
    // Listen for start of receiving a file and log progress
    FileTransfer.OnBeginRecievingFile = BeginReceivingFile;
}

void ReceivedStream(Stream stream, string identifier)
{
    Debug.Log($"Received a stream of length {stream.Length} with the Id {identifier}");
{

async void BeginReceivingFile(FileTransferProgress progressTracker)
{
    while (progressTracker.ProgressAction != ProgressAction.Finished)
    {
        Debug.Log($"Receiving file, Progress: {progressTracker.Progress}");
        
        // Wait a tenth of a second before updating progress again
        await Task.Delay(100);
    }
{
```

There are two methods to use for sending a file, and they each have an overload to use either an `object` or a `Stream`:
```csharp
FileTransferProgress SendDataToServer(NetworkConnectionToClient conn, Stream data, string identifier, Action<Result> result)
FileTransferProgress SendDataToServer(NetworkConnectionToClient conn, object data, string identifier, Action<Result> result)

FileTransferProgress SendDataToClient(NetworkConnectionToClient conn, Stream data, string identifier, Action<Result> result)
FileTransferProgress SendDataToClient(NetworkConnectionToClient conn, object data, string identifier, Action<Result> result)
```
`SendDataToServer` is used for sending data to the server (Using `[Command]`), whereas `SendDataToClient` will send data to a client (Using `[TargetRpc]`). Thus you can choose between sending your data as a Stream, which can either be a `MemoryStream` or a `FileStream` or any derived `Stream` so long as it can Seek.

Sending data as a Stream is quite simple. Here is a simple example opening a file stream and sending it:
```csharp
public void SendFileToClient(NetworkConnectionToClient conn)
{
    // Open a file from a filepath
    FileStream file = File.OpenRead("directory/filename");
    
    // Send the file 
    // We can leave the callback null, if we dont care about checking if it sent successfully
    FileTransfer.SendDataToClient(conn, file, "myFile_1", null);
}
```
Sending data as an object is even easier, provided the object can be serialized by the BinaryFormatter. Most data classes can be serialized but some fields, such as Vector3, Sprite, etc require a surrogate. This plugin already has a surrogate for Vector2, Vector3, Vector2Int, Vector3Int and Sprite. If you require more, you can add them and use the current surrogate scripts as an example. You will also need to add the surrogate into the binary formatter when it is created inside the `Gameclaw.FileTransferInternal.GetBinaryFormatter` method.

Here is a simple example:
```csharp
[System.Serializable]
public class ExampleData
{
    public int number = 0;
    public string text = "hello world!";
    public Vector3 position = new Vector3(1, 1, 1);
}

public void SendFileToClient(NetworkConnectionToClient conn)
{
    // Create example data
    var example = new ExampleData();

    // Send the object 
    FileTransfer.SendDataToClient(conn, example, "myData_1", null);
}
```
Keep in mind the delegate invoked when this `object` is received on the client will be `OnRecieveObject` and not `OnRecieveStream` like the previous example.
