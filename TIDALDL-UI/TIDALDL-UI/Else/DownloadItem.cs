﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tidal;
using AIGS.Common;
using AIGS.Helper;
using Stylet;
using TagLib;
using System.IO;

namespace TIDALDL_UI.Else
{
    public class DownloadItem : Screen
    {
        public Album TidalAlbum { get; set; }
        public Track TidalTrack { get; set; }
        public Video TidalVideo { get; set; }

        /// <summary>
        /// Item Para - OutputDir | FilePath | Track Quality | Title | Type
        /// </summary>
        public string OutputDir { get; set; }
        public string FilePath { get; set; }
        public eSoundQuality Quality { get; set; }
        public eResolution Resolution { get; set; }
        public int Index { get; set; }
        public string Title { get; set; }
        public string sType { get; set; }
        public int ErrlabelHeight { get; set; }
        public bool  OnlyM4a { get; set; }
        public string Own { get; set; }

        ///// <summary>
        ///// Progress 
        ///// </summary>
        public ProgressHelper Progress { get; set; }

        public DownloadItem(int index, Track track = null, Video video = null, Album album = null)
        {
            TidalAlbum = album;
            TidalVideo = video;
            TidalTrack = track;
            Quality    = TidalTool.getQuality(Config.Quality());
            Resolution = TidalTool.getResolution(Config.Resolution());
            OutputDir  = Config.OutputDir();
            Index      = index;
            Progress   = new ProgressHelper();
            OnlyM4a    = Config.OnlyM4a();
            Own        = album == null?null : album.Title;

            if (TidalTrack != null)
            {
                Title = track.Title;
                sType = "MusicCircle";
            }
            else
            {
                Title = video.Title;
                sType = "PlayCircle";
            }
        }


        #region Method
        public void Start()
        {
            ThreadTool.AddWork((object[] data) =>
            {
                if (Progress.GetStatus() != ProgressHelper.STATUS.WAIT)
                    return;

                ErrlabelHeight = 0;
                Progress.SetStatus(ProgressHelper.STATUS.RUNNING);
                if (TidalTrack != null)
                    DownloadTrack();
                else
                    DownloadVideo();
            });
        }

        public void Cancel()
        {
            if (Progress.GetStatus() != ProgressHelper.STATUS.COMPLETE)
                Progress.SetStatus(ProgressHelper.STATUS.CANCLE);
        }

        public void Restart()
        {
            ProgressHelper.STATUS status = Progress.GetStatus();
            if (status == ProgressHelper.STATUS.CANCLE || status == ProgressHelper.STATUS.ERROR)
            {
                Progress.Clear();
                Start();
            }
        }
        #endregion


        #region DownloadVideo
        public bool ProgressNotify(long lCurSize, long lAllSize)
        {
            Progress.Update(lCurSize, lAllSize);
            if (Progress.GetStatus() != ProgressHelper.STATUS.RUNNING)
                return false;
            return true;
        }

        public void DownloadVideo()
        {
            //GetStream
            Progress.StatusMsg = "GetStream...";
            string Errlabel = "";
            string[] TidalVideoUrls = TidalTool.getVideoDLUrls(TidalVideo.ID.ToString(), Resolution, out Errlabel);
            if (Errlabel.IsNotBlank())
                goto ERR_RETURN;
            string TsFilePath = TidalTool.getVideoPath(OutputDir, TidalVideo, TidalAlbum, ".ts");

            //Download
            Progress.StatusMsg = "Start...";
            if(!(bool)M3u8Helper.Download(TidalVideoUrls, TsFilePath, ProgressNotify))
            {
                Errlabel = "Download failed!";
                goto ERR_RETURN;
            }

            //Convert
            FilePath = TidalTool.getVideoPath(OutputDir, TidalVideo, TidalAlbum);
            if(!FFmpegHelper.IsExist())
            {
                Errlabel = "FFmpeg is not exist!";
                goto ERR_RETURN;
            }
            if (!FFmpegHelper.Convert(TsFilePath, FilePath))
            {
                Errlabel = "Convert failed!";
                goto ERR_RETURN;
            }
            System.IO.File.Delete(TsFilePath);
            Progress.SetStatus(ProgressHelper.STATUS.COMPLETE);
            return;

        ERR_RETURN:
            if (Progress.GetStatus() == ProgressHelper.STATUS.CANCLE)
                return;

            ErrlabelHeight = 15;
            Progress.SetStatus(ProgressHelper.STATUS.ERROR);
            Progress.Errmsg = Errlabel;
        }
        #endregion

        #region DownloadTrack
        public void DownloadTrack()
        {
            //GetStream
            Progress.StatusMsg = "GetStream...";
            string Errlabel = "";
            StreamUrl TidalStream = TidalTool.getStreamUrl(TidalTrack.ID.ToString(), Quality, out Errlabel);
            if (Errlabel.IsNotBlank())
                goto ERR_RETURN;
            FilePath = TidalTool.getAlbumTrackPath(OutputDir, TidalAlbum, TidalTrack, TidalStream.Url);

            //Download
            Progress.StatusMsg = "Start...";
            for (int i = 0; i < 100 && Progress.GetStatus() != ProgressHelper.STATUS.CANCLE; i++)
            {
                if ((bool)DownloadFileHepler.Start(TidalStream.Url, FilePath, Timeout: 5 * 1000, UpdateFunc: UpdateDownloadNotify, ErrFunc: ErrDownloadNotify))
                {
                    //Decrypt
                    if (!TidalTool.DecryptTrackFile(TidalStream, FilePath))
                    {
                        Errlabel = "Decrypt failed!";
                        goto ERR_RETURN;
                    }

                    if(OnlyM4a)
                    {
                        string sNewName;
                        if (!TidalTool.ConvertMp4ToM4a(FilePath, out sNewName))
                        {
                            Errlabel = "Convert mp4 to m4a failed!";
                            ErrlabelHeight = 15;
                        }
                        else
                            FilePath = sNewName;
                    }

                    //SetMetaData
                    string sLabel = TidalTool.SetMetaData(FilePath, TidalAlbum, TidalTrack, TidalTool.getAlbumCoverPath(OutputDir, TidalAlbum));
                    if (sLabel.IsNotBlank())
                    {
                        Errlabel = "Set metadata failed!";
                        goto ERR_RETURN;
                    }
                    Progress.SetStatus(ProgressHelper.STATUS.COMPLETE);
                    return;
                }
            }
            Errlabel = "Download failed!";

        ERR_RETURN:
            if (Progress.GetStatus() == ProgressHelper.STATUS.CANCLE)
                return;

            ErrlabelHeight  = 15;
            Progress.SetStatus(ProgressHelper.STATUS.ERROR);
            Progress.Errmsg = Errlabel;
        }

        public void ErrDownloadNotify(long lTotalSize, long lAlreadyDownloadSize, string sErrMsg, object data)
        {
            return;
        }

        public bool UpdateDownloadNotify(long lTotalSize, long lAlreadyDownloadSize, long lIncreSize, object data)
        {
            Progress.Update(lAlreadyDownloadSize, lTotalSize);
            if (Progress.GetStatus() != ProgressHelper.STATUS.RUNNING)
                return false;
            return true;
        }
        #endregion
    }
}
