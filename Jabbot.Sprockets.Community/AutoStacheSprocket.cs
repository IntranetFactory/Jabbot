﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jabbot.Core.Extensions;
using Jabbot.Core.Jabbr;
using Jabbot.Core.Sprockets;
using Newtonsoft.Json;

namespace Jabbot.Sprockets.Community
{
    /// <summary>
    /// Port / Mashup of the Hubot auto-stache.coffee & faceup.coffee scripts
    /// </summary>
    public class AutoStacheSprocket : RegexSprocket
    {
        private static string[] Accessories { get { return new string[] { "hipster", "clown", "mustache", "scumbag", "jason" }; } }

        public override string Name { get { return "Auto-Stache Sprocket"; } }

        public override string Description { get { return "Automatically add mustaches, hipster glasses, clown noses, scumbag hats, or Jason masks to any images it can."; } }

        public override IEnumerable<string> Usage
        {
            get
            {
                return new string[]
                {
                    "/msg <botnick> auto-stache help",
                    "Type a link to any image (png, jpg, or jpeg)"
                };
            }
        }
        
        protected override IEnumerable<Regex> PrivateMessagePatterns
        {
            get 
            { 
                return new Regex[] 
                { 
                    new Regex(@"(?i)^(auto-stache help)$"),
                }; 
            }
        }

        protected override IEnumerable<Regex> RoomMessagePatterns
        {
            get
            {
                return new Regex[] 
                {
                    new Regex(@"(?i)(?<=(>))(https?:(\S+)(png|jpg|jpeg))(?=(<))"),
                };
            }
        }

        public override void Handle(IPrivateMessage message, IJabbrClient jabbrClient)
        {
            base.Handle(message, jabbrClient);

            if (this.CanHandle(message))
            {
                if (PrivateMessagePatterns.First().Match(message.Content).Success)
                {
                    jabbrClient.PrivateReply(message.From, this.GetFormattedHelp());
                }
            }
        }

        public override void Handle(IRoomMessage message, IJabbrClient jabbrClient)
        {
            base.Handle(message, jabbrClient);

            if (this.CanHandle(message))
            {
                Task.Factory.StartNew(() =>
                {
                    var match = RoomMessagePatterns.First(p => p.Match(message.Content).Success).Match(message.Content);
                    var imageUrl = match.Groups[2].Value;
                    var stachedImageUrl = String.Format("http://faceup.me/img.jpg?overlay={0}&src={1}", Accessories.RandomElement(), Uri.EscapeDataString(imageUrl));
                    
                    jabbrClient.SayToRoom(message.Room, stachedImageUrl);
                }).ContinueWith(task =>
                {
                    var result = task.Exception;
                },
                TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    }
}
