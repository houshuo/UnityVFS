using System;
using System.IO;
using System.Collections.Generic;
using Accord.Collections;
using MiscUtil.Conversion;

public enum FileEntryState
{
    Unoccupy,
    Occupied,
}

public class FileEntry 
{
    public string filePath;
    public long offset;
    public long length;
    public long fileEntryLength;
    public FileEntryState state;

    public byte[] Serialize()
    {
        int fileBytesSize = filePath == null ? 0 : System.Text.UTF8Encoding.UTF8.GetByteCount(filePath);
        int totalByteSize =sizeof(int) + fileBytesSize + sizeof(long) + sizeof(long) + sizeof(long) + sizeof(int);
        byte[] serializeBytes = new byte[totalByteSize];
        EndianBitConverter.Big.CopyBytes(fileBytesSize, serializeBytes, 0);
        if(filePath != null)
            System.Text.UTF8Encoding.UTF8.GetBytes(filePath, 0, filePath.Length, serializeBytes, sizeof(int));
        EndianBitConverter.Big.CopyBytes(offset, serializeBytes, sizeof(int) + fileBytesSize);
        EndianBitConverter.Big.CopyBytes(length, serializeBytes, sizeof(int) + fileBytesSize + sizeof(long));
        EndianBitConverter.Big.CopyBytes(fileEntryLength, serializeBytes, sizeof(int) + fileBytesSize + sizeof(long) + sizeof(long));
        EndianBitConverter.Big.CopyBytes((int)state, serializeBytes, sizeof(int) + fileBytesSize + sizeof(long) + sizeof(long) + sizeof(long));
        return serializeBytes;
    }

    public void Deserialize(byte[] bytes)
    {
        int filePathSize = EndianBitConverter.Big.ToInt32(bytes, 0);
        filePath = filePathSize == 0 ? null : System.Text.UTF8Encoding.UTF8.GetString(bytes, sizeof(int), filePathSize);
        offset = EndianBitConverter.Big.ToInt64(bytes, sizeof(int) + filePathSize);
        length = EndianBitConverter.Big.ToInt64(bytes, sizeof(int) + filePathSize + sizeof(long));
        fileEntryLength = EndianBitConverter.Big.ToInt64(bytes, sizeof(int) + filePathSize + sizeof(long) + sizeof(long));
        state = (FileEntryState)EndianBitConverter.Big.ToInt32(bytes, sizeof(int) + filePathSize + sizeof(long) + sizeof(long) + sizeof(long));
    }
}

public class FileEntryLengthComparer : IComparer<LinkedListNode<FileEntry>>
{
    public int Compare(LinkedListNode<FileEntry> x, LinkedListNode<FileEntry> y)
    {
        return x.Value.fileEntryLength.CompareTo(y.Value.fileEntryLength);
    }
}

public class VirtualFileSystem
{
    const int AllocateMultiplier = 1024 * 2;
    //????????????
    LinkedList<FileEntry> allNodes = new LinkedList<FileEntry>();
    //????????
    RedBlackTree<LinkedListNode<FileEntry>> freeNodeTree = new RedBlackTree<LinkedListNode<FileEntry>>(new FileEntryLengthComparer());
    LinkedListNode<FileEntry> DummyEntry = new LinkedListNode<FileEntry>(new FileEntry());
    //??????????????
    Dictionary<string, LinkedListNode<FileEntry>> fileEntryDict = new Dictionary<string, LinkedListNode<FileEntry>>();
    public long indexOffset { get; private set; }

    public string fileSystemPath { private set; get; }

    static byte[] buffer = new byte[100000];

    LinkedListNode<FileEntry> FindBestFitChunk(long len)
    {
        DummyEntry.Value.fileEntryLength = len;
        RedBlackTreeNode<LinkedListNode<FileEntry>> bestChunkNode = freeNodeTree.FindGreaterThanOrEqualTo(DummyEntry);
        return bestChunkNode != null ? bestChunkNode.Value : null;
    }

    public void Load(string fileSystemPath)
    {
        this.fileSystemPath = fileSystemPath;
        if (File.Exists(this.fileSystemPath) == false) return;
        FileStream fileStream = File.OpenRead(this.fileSystemPath);
        //??????????
        fileStream.Seek(-sizeof(long), SeekOrigin.End);
        fileStream.Read(buffer, 0, sizeof(long));
        indexOffset = EndianBitConverter.Big.ToInt64(buffer, 0);
        //??????????
        fileStream.Seek(indexOffset, SeekOrigin.Begin);
        fileStream.Read(buffer, 0, sizeof(int));
        long entriesCount = EndianBitConverter.Big.ToInt32(buffer, 0);
        for (int i = 0; i < entriesCount; i++)
        {
            //????????????
            fileStream.Read(buffer, 0, sizeof(int));
            long entryLength = EndianBitConverter.Big.ToInt32(buffer, 0);
            //????????????
            if(entryLength > buffer.Length)
            {
                buffer = new byte[Math.Max(entryLength, 2 * buffer.Length)];
            }
            fileStream.Read(buffer, 0, (int)entryLength);
            FileEntry aEntry = new FileEntry();
            aEntry.Deserialize(buffer);
            //UnityEngine.Debug.Log(string.Format("{0}, offset: {1}, length: {2}, state: {3}", aEntry.filePath, aEntry.offset, aEntry.length, aEntry.state));
            //????????
            LinkedListNode<FileEntry> linkedListNode = allNodes.AddLast(aEntry);
            //????????
            if (aEntry.state == FileEntryState.Occupied)
            {
                fileEntryDict[aEntry.filePath] = linkedListNode;
            }
            else
            {
                freeNodeTree.Add(linkedListNode);
            }
        }
        fileStream.Close();
    }

    public void Save(string rootFilePath)
    {
        FileStream saveStream;
        if (File.Exists(fileSystemPath))
        {
            saveStream = File.Open(fileSystemPath, FileMode.Open, FileAccess.Write);
        }
        else
        {
            saveStream = File.OpenWrite(fileSystemPath);
        }
        

        //??????
        foreach(var fileEntry in allNodes)
        {
            if (fileEntry.state == FileEntryState.Unoccupy) continue; 
            byte[] fileBytes = File.ReadAllBytes(Path.Combine(rootFilePath, fileEntry.filePath));
            saveStream.Seek(fileEntry.offset, SeekOrigin.Begin);
            saveStream.Write(fileBytes, 0, fileBytes.Length);
        }
        //??????????
        byte[] totalEntriesCountByte = EndianBitConverter.Big.GetBytes(allNodes.Count);
        saveStream.Seek(indexOffset, SeekOrigin.Begin);
        saveStream.Write(totalEntriesCountByte, 0, totalEntriesCountByte.Length);
        //??????
        foreach (var fileEntry in allNodes)
        {
            byte[] serializedBytes = fileEntry.Serialize();
            byte[] serializedBytesCountBytes = EndianBitConverter.Big.GetBytes(serializedBytes.Length);
            //????????????
            saveStream.Write(serializedBytesCountBytes, 0, serializedBytesCountBytes.Length);
            //??????????
            saveStream.Write(serializedBytes, 0, serializedBytes.Length);
        }
        //??????????
        byte[] indexOffsetBytes = EndianBitConverter.Big.GetBytes(indexOffset);
        saveStream.Write(indexOffsetBytes, 0, indexOffsetBytes.Length);
        saveStream.Flush();
        saveStream.Close();
    }

    public void Create(string path, long len)
    {
        long entryLen = (len + AllocateMultiplier - 1) / AllocateMultiplier * AllocateMultiplier;
        
        LinkedListNode<FileEntry> bestFitNode = FindBestFitChunk(entryLen);
        if(bestFitNode != null)
        {
            long originalLength = bestFitNode.Value.fileEntryLength;

            freeNodeTree.Remove(bestFitNode);
            bestFitNode.Value.length = len;
            bestFitNode.Value.fileEntryLength = entryLen;
            bestFitNode.Value.filePath = path;
            bestFitNode.Value.state = FileEntryState.Occupied;
            fileEntryDict[path] = bestFitNode;

            FileEntry newEntry = new FileEntry();
            newEntry.offset = bestFitNode.Value.offset + entryLen;
            newEntry.length = 0;
            newEntry.fileEntryLength = originalLength - entryLen;
            newEntry.state = FileEntryState.Unoccupy;
            var remainNode = allNodes.AddAfter(bestFitNode, newEntry);
            freeNodeTree.Add(remainNode);
        }
        else
        {
            FileEntry newEntry = new FileEntry();
            newEntry.filePath = path;
            newEntry.length = len;
            newEntry.fileEntryLength = entryLen;
            newEntry.state = FileEntryState.Occupied;
            newEntry.offset = indexOffset;
            indexOffset += entryLen;
            LinkedListNode<FileEntry> newNode = allNodes.AddLast(newEntry);
            fileEntryDict[path] = newNode;
        }
    }

    public void Replace(string path, long len)
    {
        FileEntry fileEntry = Get(path);
        if(fileEntry == null)
        {
            Create(path, len);
            return;
        }

        if(fileEntry.fileEntryLength >= len)
        {
            //??????????????????????????????????
        }
        else
        {
            Delete(path);
            Create(path, len);
        }
    }

    public void Delete(string path)
    {
        if (fileEntryDict.ContainsKey(path) == false) return;
        //??????????????????
        LinkedListNode<FileEntry> linkedListNode = fileEntryDict[path];
        fileEntryDict.Remove(path);
        FileEntry fileEntry = linkedListNode.Value;
        fileEntry.filePath = null;
        fileEntry.length = 0;
        fileEntry.state = FileEntryState.Unoccupy;
        //??????????
        LinkedListNode<FileEntry> prevNode = linkedListNode.Previous;
        LinkedListNode<FileEntry> nextNode = linkedListNode.Next;

        if (prevNode != null && prevNode.Value.state == FileEntryState.Unoccupy)
        {
            allNodes.Remove(linkedListNode);//????????????????????
            freeNodeTree.Remove(prevNode);//??????????????????????
            prevNode.Value.fileEntryLength += fileEntry.fileEntryLength;//??????????????
            linkedListNode = prevNode;
            fileEntry = linkedListNode.Value;
        }

        if (nextNode != null && nextNode.Value.state == FileEntryState.Unoccupy)
        {
            allNodes.Remove(nextNode);//??????????????????????
            freeNodeTree.Remove(nextNode);//??????????????????????????
            fileEntry.fileEntryLength += nextNode.Value.fileEntryLength;
        }

        freeNodeTree.Add(linkedListNode);//??????????????????????????
    }

    public FileEntry Get(string path)
    {
        return fileEntryDict.ContainsKey(path) ? fileEntryDict[path].Value : null;
    }

    public List<string> GetAllFiles()
    {
        List<string> allFiles = new List<string>();
        foreach(var item in fileEntryDict.Values)
        {
            allFiles.Add(item.Value.filePath);
        }

        return allFiles;
    }

    public long GetAllUnoccupiedEntries()
    {
        long emptyFileLength = 0;
        foreach (var item in allNodes)
        {
            if(item.state == FileEntryState.Unoccupy)
                emptyFileLength += item.fileEntryLength;
        }

        return emptyFileLength;
    }
}
