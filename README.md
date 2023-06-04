# FileTransfer for Mirror!
This is a simple solution to send large files between the server and clients. I didn't want to host files and download them with HTTP requests when we already have a direct connection between clients. This plugin will let you send a large chunk of data directly to a client or server by utilising the connection setup already by the Mirror plugin so you dont need to pay for any additional server needs.
 
## Dependencies
If you're using Mirror for Unity as your Networking solution this plugin should work out of the box. There are no dependencies on anything except for Mirror itself. (And C# .NET of course)

# How to use it
The namespace is `Gameclaw` and the main class is `FileTransfer`. Everything has comments so should be relatively easy to decipher. Nonetheless here is a simple example and explanation to get you started. First of all, you can setup some delegates that will get invoked whenever a file transfer begins, ends, fails. They are the following:
```csharp
        public static Action<FileTransferProgress> OnBeginRecievingFile;
        public static Action<object, string> OnRecieveObject;
        public static Action<Stream, string> OnRecieveStream;
        public static Action<string> OnFailedToRecieveFile;
```
When the server or client gets it's first chunk of data for a new file transfer it will invoke `OnBeginRecievingFile`. It also gives you a `FileTransferProgress` object that you can poll to see the progress of the transfer.
Once the transfer is complete, it will either invoke `OnRecieveObject` or `OnRecieveStream` depending on which method overload for `SendDataToConnection` you decide to use.
Here's an example for creating a method to listen for receiving files:
```csharp
using Gameclaw;

void Start()
{
    // Initialize the FileTransfer plugin (Only ever need to do this once)
    FileTransfer.BeginListening();

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

There is one method for sending a file, and it has two overloads for either an `object` or a `Stream`:
```csharp
FileTransferProgress SendDataToConnection(NetworkConnection conn, Stream data, string identifier, Action<Result> result)
FileTransferProgress SendDataToConnection(NetworkConnection conn, object data, string identifier, Action<Result> result)
```
This can only send from a client to a server, or vice versa, but it cannot send directly from one client to another client (unless they're the host client). Thus you can choose between sending your data as a Stream, which can either be a `MemoryStream` or a `FileStream` or any derived `Stream` so long as it can Seek. Or as an object so long as it can be serialized by the .NET `BinaryFormatter`.

Sending data as a Stream is quite simple. Here is a simple example opening a file stream and sending it:
```csharp

void Start()
{
    // Initialize the FileTransfer plugin (Only ever need to do this once)
    FileTransfer.BeginListening();
}

public async void SendFileExample(NetworkConnection conn)
{
    // Open a file from a filepath
    FileStream file = File.OpenRead("directory/filename.txt");
    
    // Send the file 
    // We can leave the callback null if we'd rather use the 'FileTransferProgress' object
    FileTransferProgress tracker = FileTransfer.SendDataToConnection(conn, file, "myFile_1", null);

    // Now we can poll the tracker to see the progress
    while (tracker.ProgressAction != ProgressAction.Finished)
    {
        Debug.Log($"Sending file, Progress: {progressTracker.Progress}");
        
        // Wait a tenth of a second before updating progress again
        await Task.Delay(100);
    }
}
```
Sending data as an object is even easier, provided the object can be serialized by the BinaryFormatter. Most data classes can be serialized but some fields, such as `Vector3`, `Sprite`, etc require a surrogate. This plugin already has surrogates for `Vector2`, `Vector3`, `Vector2Int`, `Vector3Int` and `Sprite`. If you require more, you can add them and use the current surrogate scripts as an example. You will also need to add the surrogate into the binary formatter when it is created inside the `Gameclaw.FileTransferInternal.GetBinaryFormatter` method. You can copy paste one of the other lines and simply replace the type with your new surrogate type.

Here is a simple example sending an `object`:
```csharp
[System.Serializable]
public class ExampleData
{
    public int number = 0;
    public string text = "hello world!";
    public Vector3 position = new Vector3(1, 1, 1);
}

public void SendObjectExample(NetworkConnection conn)
{
    // Create example data
    var example = new ExampleData();

    // Send the object 
    FileTransferProgress tracker = FileTransfer.SendDataToConnection(conn, example, "myData_1", null);

    // Poll the tracker and display the progress
    while (tracker.ProgressAction != ProgressAction.Finished)
    {
        Debug.Log($"Sending file, Progress: {progressTracker.Progress}");
        
        // Wait a tenth of a second before updating progress again
        await Task.Delay(100);
    }
}
```
Keep in mind the delegate invoked when this `object` is received on the client will be `OnRecieveObject` and not `OnRecieveStream` like the previous example. (Refer to the first example at the top of this README for receiving a file and showing the progress)

## Failing to send
You can check the result of a transfer as well, to make sure the file has been sent correctly. When the receiver successfully deserializes the file and checks the MD5, it will inform the sender if it succeeded or failed. You can extend the above example to include a check for success:

```csharp
public void SendObjectExample(NetworkConnection conn)
{
    // Create example data
    var example = new ExampleData();

    // Send the object 
    // Give a callback that tells us if it succeeds or not
    FileTransferProgress tracker = FileTransfer.SendDataToConnection(conn, example, "myData_1", OnSentCallback);

    // Poll the tracker and display the progress
    while (tracker.ProgressAction != ProgressAction.Finished)
    {
        Debug.Log($"Sending file, Progress: {progressTracker.Progress}");
        
        // Wait a tenth of a second before updating progress again
        await Task.Delay(100);
    }
}

void OnSentCallback(Result result)
{
    if (result == Result.Succeeded)
    {
        Debug.Log("The file succeeded to send");
    }
    else
    {
        Debug.Log("The file failed to send");
    }
}

```
