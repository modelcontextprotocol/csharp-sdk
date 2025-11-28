ModelContextProtocol.Protocol.Implementation name is too generic.  Lots of things in this namespace named poorly.  That might be OK if users don't have to deal with them, but it looks like they do.


ModelClient.CompleteAsync - naming seems off.  Maybe "GetCompletion" 



static ModelClient.CreateSamplingHandler - why is this factory method on ModelClient???  Maybe should be an extension method.



ModelClient - Enumerate\* vs List\* - why have these redundant methods that return the same thing just one wrapping.  Can we just return IAE and have folks materialize that?



ModelClient.EnumerateToolsAsync - why accept JsonSerializerOptions on this call, it doesn't feel like something tied to enumerating tools.  Would it make more sense to make this a property on the client?



SetLoggingLevel(LogLevel, CancellationToken) vs SetLoggingLevel(LoggingLevel, CancellationToken) -- WHY?  Don't make these overloads, instead choose one then make conversions on the types.



SubscribeToResourceAsync - how are the notifications delivered?  This method confused me as I don't understand what it's supposed to do.  Might just need to research more.


TextContentBlock doesn't override ToString -- to get any of the data returned I need to get at protocol types, which feels wrong since those are all "raw" and not designed surface area.

Same is true for TextResourceContents.  The entire content model seems pretty rough - might be better to unify on MEAI content types.



Odd that ResourceContents is plural, but  ContentBlock is not.  Why do we even need to different sets of types for these?



Similarly ReadResourceResult has Contents, while CallToolResult has Content, but both are ILists.	



BlobResourceContents exposes content as a normal string, which means it encoded the UTF-8 to a string, creating work for GC.  Instead it should keep the UTF8 contents, and lazily decode that from base64 UTF8 to bytes (either as stream, or byte array).  The same problem exists with ContentBlock types, where base-64 data is exposed as Unicode strings.


Prompts expose arguments as types, whereas tools expose arguments as json schema.



