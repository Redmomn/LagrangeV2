using System.Buffers;
using System.Security.Cryptography;
using Lagrange.Core.Exceptions;
using Lagrange.Core.Internal.Events.Message;
using Lagrange.Core.Internal.Events.System;
using Lagrange.Core.Internal.Packets.Service;
using Lagrange.Core.Utility;
using Lagrange.Core.Utility.Cryptography;

namespace Lagrange.Core.Internal.Logic;

internal class OperationLogic(BotContext context) : ILogic
{
    public async Task<bool> SendFriendFile(long targetUin, Stream fileStream, string? fileName)
    {
        if (fileName == null)
        {
            if (fileStream is FileStream file)
            {
                fileName = Path.GetFileName(file.Name);
            }
            else
            {
                Span<byte> bytes = stackalloc byte[16];
                Random.Shared.NextBytes(bytes);
                fileName = Convert.ToHexString(bytes);
            }
        }

        var friend = await context.CacheContext.ResolveFriend(targetUin) ?? throw new InvalidTargetException(targetUin);
        var request = new FileUploadEventReq(friend.Uid, fileStream, fileName);
        var result = await context.EventContext.SendEvent<FileUploadEventResp>(request);

        var buffer = ArrayPool<byte>.Shared.Rent(10 * 1024 * 1024);
        int payload = request.FileStream.Read(buffer);
        var md510m = MD5.HashData(buffer[..payload]);
        ArrayPool<byte>.Shared.Return(buffer);
        request.FileStream.Seek(0, SeekOrigin.Begin);
        
        if (!result.IsExist)
        {
            var ext = new FileUploadExt
            {
                Unknown1 = 100,
                Unknown2 = 1,
                Entry = new FileUploadEntry
                {
                    BusiBuff = new ExcitingBusiInfo { SenderUin = context.Keystore.Uin },
                    FileEntry = new ExcitingFileEntry
                    {
                        FileSize = fileStream.Length,
                        Md5 = request.FileMd5,
                        CheckKey = request.FileSha1,
                        Md510M = md510m,
                        Sha3 = TriSha1Provider.CalculateTriSha1(fileStream),
                        FileId = result.FileId,
                        UploadKey = result.UploadKey
                    },
                    ClientInfo = new ExcitingClientInfo
                    {
                        ClientType = 3,
                        AppId = "100",
                        TerminalType = 3,
                        ClientVer = "1.1.1",
                        Unknown = 4
                    },
                    FileNameInfo = new ExcitingFileNameInfo { FileName = fileName },
                    Host = new ExcitingHostConfig
                    {
                        Hosts = result.RtpMediaPlatformUploadAddress.Select(x => new ExcitingHostInfo
                        {
                            Url = new ExcitingUrlInfo { Unknown = 1, Host = x.Item1 },
                            Port = x.Item2
                        }).ToList()
                    }
                },
                Unknown200 = 1
            };

            bool success = await context.HighwayContext.UploadFile(fileStream, 95, ProtoHelper.Serialize(ext));
            if (!success) return false;
        }

        int sequence = Random.Shared.Next(10000, 99999);
        uint random = (uint)Random.Shared.Next();
        var sendResult = await context.EventContext.SendEvent<SendMessageEventResp>(new SendFriendFileEventReq(friend, request, result, sequence, random));
        if (sendResult.Result != 0) throw new OperationException(sendResult.Result);

        return true;
    }
}