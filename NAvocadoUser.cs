﻿using System.Dynamic;

namespace NAvocado
{ 

    public class NAvocadoUser
    {
        public string Id { get; set; }
        public string FirstName { get; set; }
        public long CreateTime { get; set; }
        public string LastName { get; set; }
        public long Birthday { get; set; }
        public object GoogleCalendarAccessToken { get; set; }
        public object GoogleCalendarRefreshToken { get; set; }
        public object GoogleCalendarPushInfo { get; set; }
        public object GoogleInfo { get; set; }
        public string Email { get; set; }
        public long LastReadTime { get; set; }
        public string CurrentCoupleId { get; set; }
        public string AvatarUrl { get; set; }
        public NAvocadoAvatarImageUrls AvatarImageUrls { get; set; }
        public bool Verified { get; set; }
        public NAvocadoUserOptions Options { get; set; }
        public object OldestValidCookie { get; set; }
        public bool Deleted { get; set; }
    }
}