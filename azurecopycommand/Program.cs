﻿//-----------------------------------------------------------------------
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

using azurecopy;
using azurecopy.Helpers;
using azurecopy.Utils;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace azurecopycommand
{
    
    public enum Action { None, NormalCopy, BlobCopy, List, ListContainers, Examples, Make }

    public delegate bool HandlerRoutine(CtrlTypes CtrlType);

    public enum CtrlTypes
    {
        CTRL_C_EVENT = 0,
        CTRL_BREAK_EVENT,
        CTRL_CLOSE_EVENT,
        CTRL_LOGOFF_EVENT = 5,
        CTRL_SHUTDOWN_EVENT
    }

    class Program
    {
        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);
        
        const string SkyDriveOAuthUrl = "https://login.live.com/oauth20_authorize.srf?client_id=00000000480EE365&scope=wl.offline_access,wl.skydrive,wl.skydrive_update&response_type=code&redirect_uri=https%3A%2F%2Flogin.live.com%2Foauth20_desktop.srf";

        const string UsageString =
           @"Usage: azurecopy
                    -examples : Displays example commands for common scenarios
                    -version: version of azurecopy
                    -v : verbose
                    -i <url>: input url
                    -o <url>: output url
                    -db : debug (show stack traces etc)
                    -d <local path>: download to filesystem before uploading to output url. (use for big blobs)
                    -blobcopy : use blobcopy API for when Azure is output url.
                    -list <url>: list blobs in bucket/container. eg. -list https://s3.amazonaws.com/mycontainer 
                    -lc <url> : list containers for account. eg -lc https://testacc.blob.core.windows.net
                    -pu : parallel upload
                    -cs : chunk size used for parallel upload (in MB).
                    -dm : Do NOT monitor progress of copy when in 'blobcopy' mode (ie -blobcopy flag was used). Program will exit before all pending copies are complete.
                    -destblobtype page|block : Destination blob type. Used when destination url is Azure and input url was NOT azure. eg S3 to Azure. 
                    -ak | -azurekey : Azure account key.
                    -s3k | -s3accesskey : S3 access key.
                    -s3sk | -s3secretkey : S3 access key secret.
                    -sak | -srcazurekey : input url Azure account key.
                    -ss3k | -srcs3accesskey : input url S3 access key.
                    -ss3sk | -srcs3secretkey : input url S3 access key secret.
                    -tak | -targetazurekey : output url Azure account key.
                    -ts3k | -targets3accesskey : output url S3 access key.
                    -ts3sk | -targets3secretkey : output url S3 access key secret.
                    -spu | -SharepointUsername : Sharepoint Online username
                    -spp | -SharepointPassword : Sharepoint Online password
                    -rd : Retry delay in seconds used when communicating with cloud storage environments.
                    -mr : Maximum number of retries for a given operation.
                    -mc <full url> : Make container/folder/directory.
                    -configonedrive : Steps through configuring of OneDrive and saves config file with new data.
                    -configdropbox : Steps through configuring of Dropbox and saves config file with new data.
           
                Note: Remember when local file system is destination/output do NOT end the directory with a \
                      When destination is Onedrive, use the url format  one://directory/file";


        const string VersionFlag = "-version";
        const string LocalDetection = "???";
        const string ExampleFlag = "-examples";
        const string VerboseFlag = "-v";
        const string InputUrlFlag = "-i";
        const string DebugFlag = "-db";
        const string OutputUrlFlag = "-o";
        const string DownloadFlag = "-d";
        const string BlobCopyFlag = "-blobcopy";
        const string ListContainerBlobsFlag = "-list";
        const string ListContainersFlag = "-lc";
        const string MonitorBlobCopyFlag = "-dm";
        const string ParallelUploadFlag = "-pu";
        const string ChunkSizeFlag = "-cs";
        const string RetryAttemptDelayInSecondsFlag = "-rd";
        const string MaxRetryAttemptsFlag = "-mr";
        const string MakeContainerFlag = "-mc";

        // only makes sense for azure destination.
        const string DestBlobType = "-destblobtype";

        // default access keys.
        const string AzureAccountKeyShortFlag = "-ak";
        const string AWSAccessKeyIDShortFlag = "-s3k";
        const string AWSSecretAccessKeyIDShortFlag = "-s3sk";

        const string AzureAccountKeyFlag = "-azurekey";
        const string AWSAccessKeyIDFlag = "-s3accesskey";
        const string AWSSecretAccessKeyIDFlag = "-s3secretkey";

        // source access keys
        const string SourceAzureAccountKeyShortFlag = "-sak";
        const string SourceAWSAccessKeyIDShortFlag = "-ss3k";
        const string SourceAWSSecretAccessKeyIDShortFlag = "-ss3sk";

        const string SourceAzureAccountKeyFlag = "-srcazurekey";
        const string SourceAWSAccessKeyIDFlag = "-srcs3accesskey";
        const string SourceAWSSecretAccessKeyIDFlag = "-srcs3secretkey";

        // target access keys
        const string TargetAzureAccountKeyShortFlag = "-tak";
        const string TargetAWSAccessKeyIDShortFlag = "-ts3k";
        const string TargetAWSSecretAccessKeyIDShortFlag = "-ts3sk";

        const string TargetAzureAccountKeyFlag = "-targetazurekey";
        const string TargetAWSAccessKeyIDFlag = "-targets3accesskey";
        const string TargetAWSSecretAccessKeyIDFlag = "-targets3secretkey";

        // sharepoint
        const string SharepointUsernameShortFlag = "-spu";
        const string SharepointPasswordShortFlag = "-spp";
        const string SharepointUsernameFlag = "-sharepointusername";
        const string SharepointPasswordFlag = "-sharepointpassword";

        // skydrive 
        const string SkyDriveCodeFlag = "-skydrivecode";
        const string ConfigOneDriveFlag = "-configonedrive";

        // dropbox
        const string ConfigDropboxFlag = "-configdropbox";

        static UrlType _inputUrlType;
        static UrlType _outputUrlType;

        static string _inputUrl;
        static string _outputUrl;
        static Action _action = Action.None;
        static bool _listContainer = false;
        static bool _makeContainer = false;
        static bool DebugMode = false;

        // destination blob...  can only assign if source is NOT azure and destination IS azure.
        static DestinationBlobType _destinationBlobType = DestinationBlobType.Unknown;
        
        // list of copyid's used for blobcopy API.
        // will be required if want to cancel copy.
        static List<BlobCopyData> blobCopyDataList = new List<BlobCopyData>();
        
        static string GetArgument(string[] args, int i)
        {
            if (i < args.Length)
            {
                return args[i];
            }
            else
            {
                throw new ArgumentException("Invalid parameters...");
            }
        }

        static UrlType GetUrlType(string url)
        {
            UrlType urlType = UrlType.Local;

            if (AzureHelper.MatchHandler(url))
            {
                urlType = UrlType.Azure;
            }
            else if (AzureHelper.MatchFileHandler(url))
            {
                urlType = UrlType.AzureFile;
            }
            else if (S3Helper.MatchHandler(url))
            {
                urlType = UrlType.S3;
            }
            else if (SkyDriveHelper.MatchHandler(url))
            {
                urlType = UrlType.SkyDrive;
            } else  if (SharepointHelper.MatchHandler(url))
            {
                urlType = UrlType.Sharepoint;
            } 
            else if (DropboxHelper.MatchHandler(url))
            {
                urlType = UrlType.Dropbox;
            } else
            {
                urlType = UrlType.Local;  // local filesystem.
            }

            return urlType;
        }

        static void ParseArguments(string[] args)
        {
            var i = 0;

            if (args.Length > 0)
            {
                while (i < args.Length)
                {
                    switch (args[i])
                    {
                        case VersionFlag:
                            Assembly Reference = typeof(azurecopy.AzureHandler).Assembly;
                            Version Version = Reference.GetName().Version;
                            Console.WriteLine(Version.ToString());
                            break;

                        case ExampleFlag:
                            _action = Action.Examples;
                            break;

                        case VerboseFlag:
                            ConfigHelper.Verbose = true;
                            break;

                        case ParallelUploadFlag:
                            i++;
                            ConfigHelper.ParallelFactor = Convert.ToInt32(GetArgument(args, i));

                            break;

                        case RetryAttemptDelayInSecondsFlag:
                            i++;
                            ConfigHelper.RetryAttemptDelayInSeconds = Convert.ToInt32(GetArgument(args, i));
                            break;

                        case MaxRetryAttemptsFlag:
                            i++;
                            ConfigHelper.MaxRetryAttempts = Convert.ToInt32(GetArgument(args, i));
                            break;

                        case ChunkSizeFlag:
                            i++;
                            ConfigHelper.ChunkSizeInMB = Convert.ToInt32(GetArgument(args, i));

                            break;

                        case SkyDriveCodeFlag:
                            i++;
                            ConfigHelper.SkyDriveCode = GetArgument(args, i);
                            break;

                        // if we have this flag, then we simply want to redirect user to a given url.
                        // then prompt for code (response from browser). Then save it to the app.config file.
                        // will do similar for dropbox.
                        case ConfigOneDriveFlag:
                            ConfigureOneDrive();
                            break;

                        // if we have this flag, then we simply want to redirect user to a given url.
                        // then prompt for code (response from browser). Then save it to the app.config file.
                        case ConfigDropboxFlag:
                            ConfigureDropbox();
                            break;

                        case DestBlobType:
                            i++;
                            var destType = GetArgument(args, i);
                            if (destType == "page")
                            {
                                ConfigHelper.DestinationBlobTypeSelected = DestinationBlobType.Page;
                            }
                            else if (destType == "block")
                            {
                                ConfigHelper.DestinationBlobTypeSelected = DestinationBlobType.Block;
                            }

                            break;

                        case MonitorBlobCopyFlag:
                            ConfigHelper.MonitorBlobCopy = false;
                            break;

                        case BlobCopyFlag:
                            ConfigHelper.UseBlobCopy = true;
                            _action = Action.BlobCopy;
                            break;

                        case ListContainerBlobsFlag:
                            i++;
                            _inputUrl = GetArgument(args, i);
                            _inputUrlType = GetUrlType(_inputUrl);

                            // any modification of the URL (S3)
                            _inputUrl = ModifyUrl(_inputUrl, _inputUrlType);
                            _listContainer = true;
                            _action = Action.List;
                            break;

                        case ListContainersFlag:
                            i++;
                            _inputUrl = GetArgument(args, i);
                            _inputUrl = SanitizeUrl(_inputUrl);
                            _inputUrlType = GetUrlType(_inputUrl);
                            // any modification of the URL (S3)
                            _inputUrl = ModifyUrl(_inputUrl, _inputUrlType);

                            _listContainer = true;
                            _action = Action.ListContainers;
                            break;

                        case MakeContainerFlag:
                            i++;
                            _inputUrl = GetArgument(args, i);
                            _inputUrlType = GetUrlType(_inputUrl);
                            _makeContainer = true;
                            _action = Action.Make;

                            break;


                        case SharepointUsernameFlag:
                        case SharepointUsernameShortFlag:
                            i++;
                            var username = GetArgument(args, i);
                            ConfigHelper.SharepointUsername = username;
                            break;

                        case SharepointPasswordFlag:
                        case SharepointPasswordShortFlag:
                            i++;
                            var password = GetArgument(args, i);
                            ConfigHelper.SharepointPassword = password;
                            break;


                        case AzureAccountKeyFlag:
                        case AzureAccountKeyShortFlag:
                            i++;
                            var azureKey = GetArgument(args, i);
                            ConfigHelper.AzureAccountKey = azureKey;
                            ConfigHelper.SrcAzureAccountKey = azureKey;
                            ConfigHelper.TargetAzureAccountKey = azureKey;
                            break;

                        case AWSAccessKeyIDFlag:
                        case AWSAccessKeyIDShortFlag:

                            i++;
                            var s3AccessKey = GetArgument(args, i);
                            ConfigHelper.AWSAccessKeyID = s3AccessKey;
                            ConfigHelper.SrcAWSAccessKeyID = s3AccessKey;
                            ConfigHelper.TargetAWSAccessKeyID = s3AccessKey;
                            break;

                        case AWSSecretAccessKeyIDFlag:
                        case AWSSecretAccessKeyIDShortFlag:
                            i++;
                            var s3SecretKey = GetArgument(args, i);
                            ConfigHelper.AWSSecretAccessKeyID = s3SecretKey;
                            ConfigHelper.SrcAWSSecretAccessKeyID = s3SecretKey;
                            ConfigHelper.TargetAWSSecretAccessKeyID = s3SecretKey;

                            break;

                        case SourceAzureAccountKeyFlag:
                        case SourceAzureAccountKeyShortFlag:
                            i++;
                            var srcAzureKey = GetArgument(args, i);
                            ConfigHelper.SrcAzureAccountKey = srcAzureKey;
                            break;

                        case SourceAWSAccessKeyIDFlag:
                        case SourceAWSAccessKeyIDShortFlag:

                            i++;
                            var srcS3AccessKey = GetArgument(args, i);
                            ConfigHelper.SrcAWSAccessKeyID = srcS3AccessKey;

                            break;

                        case SourceAWSSecretAccessKeyIDFlag:
                        case SourceAWSSecretAccessKeyIDShortFlag:
                            i++;
                            var srcS3SecretKey = GetArgument(args, i);
                            ConfigHelper.SrcAWSSecretAccessKeyID = srcS3SecretKey;

                            break;

                        case TargetAzureAccountKeyFlag:
                        case TargetAzureAccountKeyShortFlag:
                            i++;
                            var targetAzureKey = GetArgument(args, i);
                            ConfigHelper.TargetAzureAccountKey = targetAzureKey;
                            break;

                        case TargetAWSAccessKeyIDFlag:
                        case TargetAWSAccessKeyIDShortFlag:

                            i++;
                            var targetS3AccessKey = GetArgument(args, i);
                            ConfigHelper.TargetAWSAccessKeyID = targetS3AccessKey;

                            break;

                        case TargetAWSSecretAccessKeyIDFlag:
                        case TargetAWSSecretAccessKeyIDShortFlag:
                            i++;
                            var targetS3SecretKey = GetArgument(args, i);
                            ConfigHelper.TargetAWSSecretAccessKeyID = targetS3SecretKey;

                            break;

                        case DebugFlag:
                            DebugMode = true;
                            break;

                        case InputUrlFlag:
                            i++;
                            _inputUrl = GetArgument(args, i);
                            _inputUrlType = GetUrlType(_inputUrl);

                            // any modification of the URL (S3)
                            _inputUrl = ModifyUrl(_inputUrl, _inputUrlType);

                            if (_action == Action.None)
                            {
                                _action = Action.NormalCopy;
                            }
                            break;

                        case OutputUrlFlag:
                            i++;
                            _outputUrl = GetArgument(args, i);
                            _outputUrlType = GetUrlType(_outputUrl);

                            // any modification of the URL (S3)
                            _outputUrl = ModifyUrl(_outputUrl, _outputUrlType);
                            if (_action == Action.None)
                            {
                                _action = Action.NormalCopy;
                            }
                            break;

                        case DownloadFlag:
                            i++;
                            ConfigHelper.DownloadDirectory = GetArgument(args, i);
                            ConfigHelper.AmDownloading = true;
                            break;

                        default:
                            break;
                    }

                    i++;
                }
            }
            else
            {
                Console.WriteLine(UsageString);
            }

        }

        private static string ModifyUrl(string outputUrl, UrlType outputUrlType)
        {
            if (outputUrlType == UrlType.S3)
            {
                return S3Helper.FormatUrl(outputUrl);
            }
            else
            {
                return outputUrl;
            }
        }

        /// <summary>
        /// Currently just add trailing / 
        /// </summary>
        /// <param name="_inputUrl"></param>
        /// <returns></returns>
        private static string SanitizeUrl(string _inputUrl)
        {
            if (_inputUrl.EndsWith("/"))
            {
                return _inputUrl;
            }

            return _inputUrl + "/";
        }

        private static void ConfigureOneDrive()
        {
            Console.WriteLine("Configuring AzureCopy for Onedrive:\n\n");
            Console.WriteLine("Please open a browser and enter the URL:\n" + SkyDriveOAuthUrl+"\n");
            Console.WriteLine("Once you log in with your Microsoft Account, you'll be redirected to a url similar to https://login.live.com/oauth20_desktop.srf?code=f011cabb-e9ce-e4cc-c169-cf70eae11da8&lc=2057");
            Console.WriteLine("Please copy and paste the code parameter (in the example above, it would be 'f011cabb-e9ce-e4cc-c169-cf70eae11da8' here then press enter");
            var code = Console.ReadLine();
            Console.WriteLine("Modifying config file with code");
            Console.WriteLine("Thankyou....  enjoy AzureCopy");

            ConfigHelper.SkyDriveCode = code;
            ConfigHelper.SaveConfig();
        }

        private static void ConfigureDropbox()
        {
            var authorizeUrl = DropboxHelper.BuildAuthorizeUrl();

            Console.WriteLine("Configuring AzureCopy for Dropbox:\n\n");
            Console.WriteLine("Please open a browser and enter the URL:\n" + authorizeUrl + "\n");
            Console.WriteLine("Once you log in with your Dropbox Account, please return to this screen and press enter.\n");
            Console.ReadKey();
            Console.WriteLine("Modifying config file with code");
            Console.WriteLine("Thankyou....  enjoy AzureCopy");

            var userLoginTuple = DropboxHelper.GetAccessToken();
            ConfigHelper.DropBoxUserSecret = userLoginTuple.Item1.ToString();
            ConfigHelper.DropBoxUserToken = userLoginTuple.Item2.ToString();

            ConfigHelper.SaveConfig();
        }


        // default to local filesystem
        public static IBlobHandler GetHandler(UrlType urlType, string url)
        {
            if (DebugMode)
            {
                Console.WriteLine("GetHandler start");
            }

            IBlobHandler blobHandler;

            switch (urlType)
            {
                case UrlType.Azure:
                    blobHandler = new AzureHandler(url);
                    break;

                case UrlType.AzureFile:
                    blobHandler = new AzureFileHandler(url);
                    break;

                case UrlType.S3:
                    blobHandler = new S3Handler(url);
                    break;

                case UrlType.SkyDrive:
                    blobHandler = new SkyDriveHandler(url);
                    break;

                case UrlType.Local:
                    blobHandler = new FileSystemHandler(url);
                    break;
                
                //case UrlType.Sharepoint:
                //    blobHandler = new SharepointHandler(url);
                //    break;

                case UrlType.Dropbox:
                    blobHandler = new DropboxHandler(url);
                    break;

                default:
                    blobHandler = new FileSystemHandler(url);
                    break;
            }

            if (DebugMode)
            {
                Console.WriteLine("GetHandler retrieved " + blobHandler.GetType().ToString());
            }



            return blobHandler;
        }



        static void DoNormalCopy()
        {

            IBlobHandler inputHandler = GetHandler(_inputUrlType, _inputUrl);
            IBlobHandler outputHandler = GetHandler(_outputUrlType, _outputUrl);

            if (inputHandler != null && outputHandler != null)
            {
                // handle multiple files.
                //currently sequentially.
                var sourceBlobList = GetSourceBlobList(inputHandler);

                if (ConfigHelper.UseBlobCopy)
                {
                    AzureBlobCopyHandler.StartCopyList(sourceBlobList, _outputUrl, _destinationBlobType);
                }
                else
                {
                    foreach (var blob in sourceBlobList)
                    {
                        var fileName = "";
                        if (ConfigHelper.AmDownloading)
                        {
                            fileName = GenerateFileName(ConfigHelper.DownloadDirectory, blob.Name);
                        }

                        var sourceContainer = inputHandler.GetContainerNameFromUrl(_inputUrl);

                        // read blob
                        var inputBlob = inputHandler.ReadBlob(sourceContainer, blob.Name, fileName);

                        // if blob is marked with type "Unknown" then set it to what was passed in on command line.
                        // if nothing was passed in, then default to block?
                        if (inputBlob.BlobType == DestinationBlobType.Unknown)
                        {
                            if (_destinationBlobType != DestinationBlobType.Unknown)
                            {
                                inputBlob.BlobType = _destinationBlobType;
                            }
                            else
                            {
                                inputBlob.BlobType = DestinationBlobType.Block;
                            }
                        }

                        Console.WriteLine(string.Format("Copying blob to {0}", _outputUrl));

                        // write blob
                        var destContainerName = outputHandler.GetContainerNameFromUrl(_outputUrl);
                        var destBlobName = outputHandler.GetBlobNameFromUrl(_outputUrl);

                        // take the inputBlob name and remove the default prefix.

                        if (!string.IsNullOrEmpty(blob.BlobPrefix))
                        {
                            destBlobName += inputBlob.Name.Substring(blob.BlobPrefix.Length);
                        }
                        else
                        {
                            // if destBlobName ends with / then its a prefix.
                            // if it does not, then its an absolute blob name (ie when the blob was copied then it was also being renamed).
                            if (destBlobName.EndsWith("/"))
                            {
                                destBlobName += inputBlob.Name;
                            }
                            else
                            {
                                // if destBlobName is empty then take it from blob.
                                // else it appears we were told the name to use so leave it.
                                if (string.IsNullOrWhiteSpace(destBlobName))
                                {
                                    destBlobName = inputBlob.Name;
                                }
                                else
                                {
                                    // leave it.
                                }
                            }
                        }

                        outputHandler.WriteBlob(destContainerName, destBlobName, inputBlob, ConfigHelper.ParallelFactor, ConfigHelper.ChunkSizeInMB);

                        if (inputBlob.BlobSavedToFile)
                        {
                            if (File.Exists(inputBlob.FilePath)) File.Delete(inputBlob.FilePath);
                        }
                    }
                }
            }
        }

        private static string GenerateOutputUrl(string baseOutputUrl, string inputUrl)
        {
            var u = new Uri(inputUrl);
            var blobName = "";
            blobName = u.Segments[u.Segments.Length - 1];

            var outputPath = Path.Combine(baseOutputUrl, blobName);

            return outputPath;

        }

        /// <summary>
        /// Get all possible blobs for the handler (which had the original url passed into the constructor).
        /// </summary>
        /// <param name="inputHandler"></param>
        /// <param name="url"></param>
        /// <returns></returns>
        private static IEnumerable<BasicBlobContainer> GetSourceBlobList(IBlobHandler inputHandler)
        {
            var containerName = inputHandler.GetContainerNameFromUrl(inputHandler.GetBaseUrl());

            if (CommonHelper.IsABlob(inputHandler.GetBaseUrl()))
            {
                var blobName = inputHandler.GetBlobNameFromUrl( inputHandler.GetBaseUrl());
                var blob = new BasicBlobContainer{
                    Name = blobName,
                    DisplayName = blobName,
                    BlobType = BlobEntryType.Blob,
                    Url = inputHandler.GetBaseUrl(),
                    Container = containerName
                };

                yield return blob;
                //blobList.Add(blob);
            }
            else
            {
                var res = inputHandler.ListBlobsInContainer(containerName);
                foreach( var i in res)
                {
                    yield return i;
                }
            }
        }

        static void DoListContainers()
        {
            IBlobHandler handler = GetHandler(_inputUrlType, _inputUrl);

            try
            {
                var containerList = handler.ListContainers(_inputUrl);

                foreach (var container in containerList)
                {
                    Console.WriteLine(string.Format("{0}", container.DisplayName));
                }
            }
            catch(NotImplementedException ex)
            {
                Console.WriteLine("Unfortunately Listing containers/directories is not supported for this cloud platform yet");
            }
        }


        static void DoList(bool debugMode)
        {
            IBlobHandler handler = GetHandler(_inputUrlType, _inputUrl);

            var containerName = handler.GetContainerNameFromUrl(_inputUrl);

            if (DebugMode)
            {
                Console.WriteLine("container name " + containerName);
            }

            var blobPrefix = handler.GetBlobNameFromUrl(_inputUrl);
            var blobList = handler.ListBlobsInContainer(containerName, blobPrefix, DebugMode);

            foreach (var blob in blobList)
            {
                Console.WriteLine( string.Format("{0}  ({1} {2})", blob.DisplayName, blob.Name, blob.Url));
            }
        }

        static void Process(bool debugMode)
        {
            switch (_action)
            {
                case Action.Examples:
                    DisplayExamples();
                    break;

                case Action.List:
                    DoList(debugMode);
                    break;

                case Action.ListContainers:
                    DoListContainers();
                    break;
                case Action.Make:
                    DoMake();
                    break;

                case Action.NormalCopy:
                case Action.BlobCopy:
                    DoNormalCopy();
                    break;

                default:
                    break;
            }

        }

        private static void DoMake()
        {
            IBlobHandler handler = GetHandler(_inputUrlType, _inputUrl);
            var containerName = handler.GetContainerNameFromUrl(_inputUrl);
            handler.MakeContainer(containerName);
        }

        private static void DisplayExamples()
        {
            Console.WriteLine("AzureCopy Common Usage Examples\n\n");
            Console.WriteLine("(assumption that the config file is correctly configured with Azure/S3/etc identifiers and keys)\n");
            Console.WriteLine("\n");
            Console.WriteLine("\nS3 to Azure using regular copy:\n azurecopy.exe -i https://mybucket.s3.amazonaws.com/myblob -o https://myaccount.blob.core.windows.net/mycontainer");
            Console.WriteLine("\nS3 to Azure using blob copy api (better for local bandwidth:\n azurecopy.exe -i https://mybucket.s3.amazonaws.com/myblob -o https://myaccount.blob.core.windows.net/mycontainer -blobcopy");
            Console.WriteLine("\nAzure to S3:\n azurecopy.exe -i https://myaccount.blob.core.windows.net/mycontainer/myblob -o https://mybucket.s3.amazonaws.com/ ");
            Console.WriteLine("\nList contents in S3 bucket:\n azurecopy.exe -list https://mybucket.s3.amazonaws.com/");
            Console.WriteLine("\nList contents in Azure container:\n azurecopy.exe -list https://myaccount.blob.core.windows.net/mycontainer/ ");
            Console.WriteLine("\nOnedrive to local using regular copy:\n azurecopy.exe -i sky://temp/myfile.txt -o c:\\temp\\");
            Console.WriteLine("\nDropbox to local using regular copy:\n azurecopy.exe -i https://dropbox.com/temp/myfile.txt -o c:\\temp\\");

            
        }

        private static string GenerateFileName(string downloadDirectory, string blobName)
        {
            var fullPath = Path.Combine(downloadDirectory, blobName);
            return fullPath;
        }

        private static bool ConsoleCtrlCheck(CtrlTypes ctrlType)
        {
            // abort all blobcopy operations.
            if (blobCopyDataList.Any())
            {
                try
                {
                    Console.WriteLine("Aborting blob copy operations");

                    var taskList = new List<Task>();
                    // abort all.
                    foreach (var bcd in blobCopyDataList)
                    {
                        try
                        {
                            var t = bcd.Blob.AbortCopyAsync(bcd.CopyID);
                            taskList.Add(t);

                        }
                        catch(Exception ex)
                        {
                            Console.WriteLine("inner exception");
                        }
                    }

                    Task.WaitAll(taskList.ToArray());
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Unable to abort copying. Possibly completed already");
                    Environment.Exit(1);
                }
            }

            Environment.Exit(0);

            return true;
        }

        static void Main(string[] args)
        {
            ParseArguments(args);

            var sw = new Stopwatch();

            try
            {
                SetConsoleCtrlHandler(new HandlerRoutine(ConsoleCtrlCheck), true);

                sw.Start();
                Process(DebugMode);
                sw.Stop();
                Console.WriteLine("Operation took {0} ms", sw.ElapsedMilliseconds);
            }
            catch(Exception ex)
            {
                Console.WriteLine("Unknown error generated. Please report to Github page https://github.com/kpfaulkner/azurecopy/issues .  Can view underlying stacktrace by adding -db flag. " + ex.ToString());

                if (DebugMode)
                {
                    Console.WriteLine(ex.StackTrace.ToString());
                }
            }
        }


    }
}
