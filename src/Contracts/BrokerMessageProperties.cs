namespace Contracts;

// Names of the broker application properties: metadata that travels next to the JSON body, never
// inside it. They are diagnostic only; no service may depend on them to process a message.
public static class BrokerMessageProperties
{
    // Host name of the machine that published the message. It lets a consumer notice that a message
    // came from somewhere else, which is what happens when several developers run the project
    // against the same subscription and their consumers compete for the same messages.
    public const string PublisherHost = "PublisherHost";
}
