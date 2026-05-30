# Photon Serialization Oracle API (Phase 1)

The real `Photon3Unity3D.dll` is referenced **test-only** in `BlackIce.Photon.Tests` to
produce reference bytes. Never shipped (GPL).

Namespace: `ExitGames.Client.Photon`. Protocol: **GpBinaryV18** (`Protocol18 : IProtocol`,
`ProtocolType => "GpBinaryV18"`). Construct with `new Protocol18()`.

## Entry points
- Scalars/collections (type byte + value): `byte[] IProtocol.Serialize(object obj)` — base
  convenience that calls `Serialize(StreamBuffer, obj, setType: true)`.
- Operation request: `void SerializeOperationRequest(StreamBuffer, byte operationCode, Dictionary<byte,object> parameters, bool setType)`.
- Event: `void SerializeEventData(StreamBuffer, EventData, bool setType)`.
- Response: `void SerializeOperationResponse(StreamBuffer, OperationResponse, bool setType)`.
- Deserialize value: `object IProtocol.Deserialize(byte[] serializedData)`.

## StreamBuffer
`new StreamBuffer(64)`; `byte[] ToArray()`; `int Length`; `byte ReadByte()`;
`StreamBuffer(byte[] buf)` to read.

## Open byte-level questions (reconcile against oracle in Tasks 3–5)
- v1.8 string length prefix width (int16 vs varint) — Task 3.
- `Dictionary<byte,object>` header layout — Task 4.
- Whether full wire messages carry a `0xF3` (243) magic + message-type byte prefix before the
  operation code (see `DeserializeMessage`) — Task 5. Our `MessageSerializer` must match
  whatever `SerializeOperationRequest` emits.

## Crypto
DH/encryption code lives in `ExitGames.Client.Photon.Encryption` — referenced for the Task 7
spike.
