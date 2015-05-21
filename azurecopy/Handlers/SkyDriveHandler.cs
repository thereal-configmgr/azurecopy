﻿﻿//-----------------------------------------------------------------------
// <copyright >
//    Copyright 2013 Ken Faulkner
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//      http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>
//-----------------------------------------------------------------------

using azurecopy.Helpers;
using azurecopy.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace azurecopy
{
    public class SkyDriveHandler : IBlobHandler
    {

        private string accessToken;
        private string baseUrl = "";

        // store so we dont have to keep retrieving it.
        private static Datatypes.SkyDriveDirectory destinationDirectory = null;

        public SkyDriveHandler( string url=null)
        {
            accessToken = SkyDriveHelper.GetAccessToken();
            baseUrl = url;
        }

        public string GetBaseUrl()
        {
            return baseUrl;
        }

        public void MoveBlob(string startUrl, string finishUrl)
        {


        }

        // override configuration. 
        public void OverrideConfiguration(Dictionary<string, string> configuration)
        {
            throw new NotImplementedException("OverrideConfiguration not implemented yet");
        }

        // make container
        // assumption being last part of url is the new container.
        public void MakeContainer(string url)
        {
            url = url.Replace(SkyDriveHelper.OneDrivePrefix, "");

            var targetDirectory = SkyDriveHelper.CreateFolder(url);

        }

        public List<BasicBlobContainer> ListContainers(string baseUrl)
        {
            throw new NotImplementedException("Onedrive list containers not implemented");
        }


        public Blob ReadBlob(string url, string filePath = "")
        {
            Blob blob = new Blob();
            string blobName = "";

            StringBuilder requestUriFile;

            if (url.IndexOf(SkyDriveHelper.OneDrivePrefix) != -1)
            {
                url = url.Replace(SkyDriveHelper.OneDrivePrefix, "");

                var skydriveFileEntry = SkyDriveHelper.GetSkyDriveEntryByFileNameAndDirectory(url);
                requestUriFile = new StringBuilder(skydriveFileEntry.Source);
                var sp = url.Split('/');
                blobName = sp[sp.Length - 1];
            }
            else
            {
                // get blob name from url then remove it.
                // ugly ugly hack.
                var sp = url.Split('=');
                blobName = sp.Last();

                var lastIndex = url.LastIndexOf("&");

                requestUriFile = new StringBuilder(url.Substring(0, lastIndex));
                // manipulated url to include blobName="real blob name". So parse end off url.
                // ugly hack... but needed a way for compatibility with other handlers.
            }

            requestUriFile.AppendFormat("?access_token={0}", accessToken);
            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(requestUriFile.ToString());
            request.Method = "GET";

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            var s = response.GetResponseStream();

            // get stream to store.
            using (var stream = CommonHelper.GetStream(filePath))
            {
                byte[] data = new byte[32768];
                int bytesRead = 0;
                do
                {
                    bytesRead = s.Read(data, 0, data.Length);
                    stream.Write(data, 0, bytesRead);
                }
                while (bytesRead > 0);

                if (!blob.BlobSavedToFile)
                {
                    var ms = stream as MemoryStream;
                    blob.Data = ms.ToArray();
                }

            }

            blob.Name = blobName;
            blob.BlobOriginType = UrlType.SkyDrive;
            return blob;
        }

        // url simply is <directory>/filename   format. NOT the entire/real url.
        // will make directories if they do not already exist.
        public void WriteBlob(string url, Blob blob,  int parallelUploadFactor=1, int chunkSizeInMB=4)
        {
            url = url.Replace( SkyDriveHelper.OneDrivePrefix, "");

            if (destinationDirectory == null)
            {
                // check if target folder exists.
                // if not, create it.
                destinationDirectory = SkyDriveHelper.GetSkyDriveDirectory(url);

                if (destinationDirectory == null)
                {
                    destinationDirectory = SkyDriveHelper.CreateFolder(url);
                }
            }
           
            var blobName = blob.Name;

            var urlTemplate = @"https://apis.live.net/v5.0/{0}/files/{1}?access_token={2}";
            var requestUrl = string.Format(urlTemplate, destinationDirectory.Id, blobName, accessToken);

            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(requestUrl);
            request.Method = "PUT";
            Stream dataStream = request.GetRequestStream();
            Stream inputStream = null;

            // get stream to data.
            if (blob.BlobSavedToFile)
            {
                inputStream = new FileStream(blob.FilePath, FileMode.Open);
            }
            else
            {
                inputStream = new MemoryStream(blob.Data);
            }

            int bytesRead;
            int readSize = 64000;
            int totalSize = 0;
            byte[] arr = new byte[readSize];
            do
            {
                bytesRead = inputStream.Read( arr,0, readSize);
                if (bytesRead > 0)
                {
                    totalSize += bytesRead;
                    dataStream.Write(arr, 0, bytesRead);
                }
            }
            while (  bytesRead > 0);
         
            dataStream.Close();
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            response.Close();
        }


        // assuming only single dir.
        // url == directory/blobname
        private string GetBlobNameFromUrl(string url)
        {
            var sp = url.Split('/');
            return sp[3];
        }

        // assuming only single dir.
        // url == directory/blobname
        private string GetDirectoryNameFromUrl(string url)
        {
            var sp = url.Split('/');
            return sp[2];
        }


        public List<BasicBlobContainer> ListBlobsInContainer(string container)
        {
            var blobList = new List<BasicBlobContainer>();

            var skydriveListing = SkyDriveHelper.ListSkyDriveDirectoryContent(container);
            foreach (var skyDriveEntry in skydriveListing)
            {
                var blob = new BasicBlobContainer();
                blob.Name = skyDriveEntry.Name;

                var resolvedOneDriveEntry = SkyDriveHelper.ListSkyDriveFileWithUrl(skyDriveEntry.Id);

                // keep display name same as name until determine otherwise.
                blob.DisplayName = blob.Name;
                blob.Container = container;
                blob.Url = string.Format("{0}&blobName={1}", resolvedOneDriveEntry.Source, blob.Name);       // modify link so we can determine blob name purely from link.             
                blobList.Add(blob);
            }
            return blobList;

        }

        private string GetSkyDriveDirectoryId(string directoryName)
        {
            var skydriveListing = SkyDriveHelper.ListSkyDriveRootDirectories();

            var skydriveId = (from e in skydriveListing where e.Name == directoryName select e.Id).FirstOrDefault();

            return skydriveId;

        }

        // not passing url. Url will be generated behind the scenes.
        public Blob ReadBlobSimple(string container, string blobName, string filePath = "")
        {
            if (baseUrl == null)
            {
                throw new ArgumentNullException("Constructor needs base url passed");
            }

            var url = baseUrl + "/" + container + "/" + blobName;
            return ReadBlob(url, filePath);
        }

        // not passing url.
        public void WriteBlobSimple(string container, Blob blob, int parallelUploadFactor = 1, int chunkSizeInMB = 4)
        {
            if (baseUrl == null)
            {
                throw new ArgumentNullException("Constructor needs base url passed");
            }

            var url = baseUrl + "/" + container + "/";
            WriteBlob(url, blob, parallelUploadFactor, chunkSizeInMB);
        }

        // not required to pass full url.
        public List<BasicBlobContainer> ListBlobsInContainerSimple(string container)
        {
            if (baseUrl == null)
            {
                throw new ArgumentNullException("Constructor needs base url passed");
            }

            var url = baseUrl + "/" + container + "/";
            return ListBlobsInContainer(url);
        }

        public void MakeContainerSimple(string container)
        {
            throw new NotImplementedException("MakeContainerSimple not implemented");
        }


    }
}