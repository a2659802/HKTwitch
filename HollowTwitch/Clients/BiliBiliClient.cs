﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using HollowTwitch.Commands;
using UnityEngine;

namespace HollowTwitch.Clients
{
    public struct Message
    {
        public string user;
        public string time;
        public string text;
        public Message(string nickname, string timeline, string text)
        {
            this.user = nickname;
            this.time = timeline;
            string tmp = text;

            if(tmp != null)
                tmp = tmp.Trim().Replace("@", "!").Replace("！", "!").Replace("+", " ");

            if (tmp != null && tmp.Length > 1 && tmp.StartsWith(TwitchMod.Instance.GetPrefix()))
            {
                foreach (var kv in ChineseCommand.CmdTranslation)
                {
                    if (tmp.Contains(kv.Key))
                    {
                        tmp = tmp.Replace(kv.Key, kv.Value);
                    }
                }
                this.text = tmp;
            }
            else
            {
                this.text = text;
            }
        }
    }
    internal class BiliBiliClient : IClient
    {
        
        private readonly string url;
        private readonly Dictionary<string, string> data;
        private readonly List<Message> log = new List<Message>();
        public readonly Dictionary<string, int> userid = new Dictionary<string, int>();
        public event Action<string, string> ChatMessageReceived;
        public event Action<string> ClientErrored;
        public event Action<string> RawPayload;
        private static TwitchConfig _config;
        public BiliBiliClient(TwitchConfig config)
        {
            url = "https://api.live.bilibili.com/xlive/web-room/v1/dM/gethistory";
            data = new Dictionary<string, string>
            {
                {"roomid",config.Channel },
                {"csrf_token","" },
                {"csrf","" },
                {"visit_id","" },
            };

            RawPayload += ProcessJson;

            _config = config;
        }
        
        public void Dispose()
        {
            ClientErrored?.Invoke("BiliBili Client Closed");
        }
        public static bool MyRemoteCertificateValidationCallback(System.Object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            bool isOk = true;
            // If there are errors in the certificate chain,
            // look at each error to determine the cause.
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                for (int i = 0; i < chain.ChainStatus.Length; i++)
                {
                    if (chain.ChainStatus[i].Status == X509ChainStatusFlags.RevocationStatusUnknown)
                    {
                        continue;
                    }
                    chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                    chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0);
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
                    bool chainIsValid = chain.Build((X509Certificate2)certificate);
                    if (!chainIsValid)
                    {
                        isOk = false;
                        break;
                    }
                }
            }
            return isOk;
        }
        public void StartReceive()
        {
            ServicePointManager.ServerCertificateValidationCallback = MyRemoteCertificateValidationCallback;

            while (true)
            {
                Thread.Sleep(1000);
                try
                {
                    var message = Post(url, data);
                    RawPayload?.Invoke(message);
                }
                catch (Exception e)
                {
                    ClientErrored?.Invoke("Error occured trying to read stream: " + e);
                }
            
            }
        }
        private bool timeOut(Message m)
        {
            return ( (DateTime.Now - Convert.ToDateTime(m.time)).TotalSeconds > 30 );
        }
        private void ProcessJson(string json)
        {
            if (json != null)
            {
                DanmuJson.Root rt = JsonConvert.DeserializeObject<DanmuJson.Root>(json);
                var room = rt.data.room;
                foreach (var r in room)
                {
                    var m = new Message(r.nickname, r.timeline, r.text);
                    if (!log.Contains(m))
                    {
                        log.Add(m);

                        if (timeOut(m)) // skip command history
                        {
                            continue;
                        }
                        ChatMessageReceived?.Invoke(m.user, m.text);

                        if (!userid.ContainsKey(m.user))
                        {
                            userid[m.user] = r.uid;
                        }
                    }

                }
            }
            if(log.Count > 1000)
            {
                log.RemoveRange(0, 800);
            }
            
        }
        public static string Post(string url, Dictionary<string, string> dic)
        {
            string result = "";
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            #region 添加Post 参数
            StringBuilder builder = new StringBuilder();
            int i = 0;
            foreach (var item in dic)
            {
                if (i > 0)
                    builder.Append("&");
                builder.AppendFormat("{0}={1}", item.Key, item.Value);
                i++;
            }
            byte[] data = Encoding.UTF8.GetBytes(builder.ToString());
            req.ContentLength = data.Length;
            using (Stream reqStream = req.GetRequestStream())
            {
                reqStream.Write(data, 0, data.Length);
                reqStream.Close();
            }
            #endregion
            HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
            Stream stream = resp.GetResponseStream();
            //获取响应内容
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                result = reader.ReadToEnd();
            }
            return result;
        }
        public static string Get(string url, Dictionary<string, string> dic)
        {
            string result = "";
            StringBuilder builder = new StringBuilder();
            builder.Append(url);
            if (dic.Count > 0)
            {
                builder.Append("?");
                int i = 0;
                foreach (var item in dic)
                {
                    if (i > 0)
                        builder.Append("&");
                    builder.AppendFormat("{0}={1}", item.Key, item.Value);
                    i++;
                }
            }
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(builder.ToString());
            //添加参数
            HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
            Stream stream = resp.GetResponseStream();
            try
            {
                //获取内容
                using (StreamReader reader = new StreamReader(stream))
                {
                    result = reader.ReadToEnd();
                }
            }
            finally
            {
                stream.Close();
            }
            return result;

        }

        public string GetFace(string user)
        {
            if (!userid.ContainsKey(user))
                return null;

            var json = Get("http://api.bilibili.com/x/space/acc/info", new Dictionary<string, string> { { "mid", userid[user].ToString() } });
            if (json.Length > 100)
            {
                string tz = "\"face\":\"";
                var img_url_start_idx = json.IndexOf(tz);
                var sub = json.Substring(img_url_start_idx + tz.Length);
                var end_idx = sub.IndexOf("\"");
                sub = sub.Substring(0, end_idx);

                return sub;
            }

            return null;
        }
        
        public (List<Message>,Dictionary<string,int>,string) GetStatic()
        {
            return (log, userid,_config.Channel);
        }
    }
}


namespace DanmuJson
{

    //如果好用，请收藏地址，帮忙分享。
    public class Check_info
    {
        /// <summary>
        /// 
        /// </summary>
        public int ts { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string ct { get; set; }
    }

    public class AdminItem
    {
        /// <summary>
        /// 游戏：『糖豆人：终极淘汰赛』 平台：Steam、PS4
        /// </summary>
        public string text { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int uid { get; set; }
        /// <summary>
        /// 席子酱-Caxiz
        /// </summary>
        public string nickname { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string uname_color { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string timeline { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int isadmin { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int vip { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int svip { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<string> medal { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<string> title { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<string> user_level { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int rank { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int teamid { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string rnd { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string user_title { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int guard_level { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int bubble { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string bubble_color { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public Check_info check_info { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int lpl { get; set; }
    }

    public class RoomItem
    {
        /// <summary>
        /// 搓大澡舒服吗
        /// </summary>
        public string text { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int uid { get; set; }
        /// <summary>
        /// 守夜冠军007
        /// </summary>
        public string nickname { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string uname_color { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string timeline { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int isadmin { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int vip { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int svip { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<string> medal { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<string> title { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<string> user_level { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int rank { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int teamid { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string rnd { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string user_title { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int guard_level { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int bubble { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string bubble_color { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public Check_info check_info { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int lpl { get; set; }
    }

    public class Data
    {
        /// <summary>
        /// 
        /// </summary>
        public List<AdminItem> admin { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<RoomItem> room { get; set; }
    }

    public class Root
    {
        /// <summary>
        /// 
        /// </summary>
        public int code { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public Data data { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string message { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string msg { get; set; }
    }



}
