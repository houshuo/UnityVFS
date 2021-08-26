using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public class VFSPacker 
{
    [MenuItem("Tools/BuildVFSPackage")]
    static void BuildVFSPakcage()
    {
        VirtualFileSystem vfs = new VirtualFileSystem();
        vfs.Load(string.Format("{0}_{1}.dh2", new DirectoryInfo(Application.dataPath).Parent.Parent.FullName + "/LatestRes/PackagedResVFS", LoadManager.platformName));
        var packagedResPath = string.Format("{0}/{1}/data", new DirectoryInfo(Application.dataPath).Parent.Parent.FullName + "/LatestRes/PackagedRes", LoadManager.platformName);
        //1.先把删除的文件从vfs里面删除
        var allFilesInVFS = vfs.GetAllFiles();
        foreach(var filePath in allFilesInVFS)
        {
            if(File.Exists(Path.Combine(packagedResPath, filePath)) == false)
            {
                vfs.Delete(filePath);
            }
        }

        //2.把所有的文件Replace一遍
        string[] files = Directory.GetFiles(packagedResPath, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            string relativePath = file.Replace(packagedResPath + "\\", "").Replace("\\", "/");
            FileInfo fileInfo = new FileInfo(file);
            vfs.Replace(relativePath, fileInfo.Length);
        }

        vfs.Save(packagedResPath);
    }

    [MenuItem("Tools/TestVFSPackage")]
    static void TestVFSPakcage()
    {
        VirtualFileSystem vfs = new VirtualFileSystem();
        string fileSystemPath = string.Format("{0}_{1}.dh2", new DirectoryInfo(Application.dataPath).Parent.Parent.FullName + "/LatestRes/PackagedResVFS", LoadManager.platformName);
        vfs.Load(fileSystemPath);
        var packagedResPath = string.Format("{0}/{1}/data", new DirectoryInfo(Application.dataPath).Parent.Parent.FullName + "/LatestRes/PackagedRes", LoadManager.platformName);
        string[] files = Directory.GetFiles(packagedResPath, "*", SearchOption.AllDirectories);

        Stream fileSystemStream = File.OpenRead(fileSystemPath);
        foreach (var file in files)
        {
            Stream originFileStream = File.OpenRead(file);
            string md5Origin = MD5Helper.ComputeMD5(originFileStream);
            originFileStream.Close();

            string relativePath = file.Replace(packagedResPath + "\\", "").Replace("\\", "/");
            FileEntry fileEntry = vfs.Get(relativePath);
            byte[] fileBytes = new byte[fileEntry.length];
            fileSystemStream.Seek(fileEntry.offset, SeekOrigin.Begin);
            fileSystemStream.Read(fileBytes, 0, (int)fileEntry.length);
            string md5VFS = MD5Helper.ComputeMD5(fileBytes);

            if (md5Origin != md5VFS)
            {
                Debug.Log(string.Format("{0} mismatch", relativePath));
            }
        }

        var allFilesInVFS = vfs.GetAllFiles();
        foreach (var filePath in allFilesInVFS)
        {
            Debug.Log(string.Format("Find File: {0}", filePath));
            if (File.Exists(Path.Combine(packagedResPath, filePath)) == false)
            {
                Debug.Log(string.Format("{0} exist in vfs not in file system", filePath));
            }
        }
        Debug.Log("Empty File Length: " + vfs.GetAllUnoccupiedEntries());

        fileSystemStream.Close();
    }
}
