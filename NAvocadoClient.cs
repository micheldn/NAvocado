﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NAvocado.Exceptions;
using NAvocado.Extensions;

namespace NAvocado
{
    public class NAvocadoClient
    {
        /// <summary>
        ///     UserAgent that is send with the request, identifying that an application used this library.
        /// </summary>
        public const string UserAgent = "NAvocado v0.0.1";

        public const string ApiUrlBase = "https://avocado.io/api/";
        public const string ApiUrlLogin = ApiUrlBase + "authentication/login";
        public const string ApiUrlLogout = ApiUrlBase + "authentication/logout";
        public const string ApiUrlUser = ApiUrlBase + "user/";
        public const string ApiUrlCouple = ApiUrlBase + "couple/";
        public const string ApiUrlActivities = ApiUrlBase + "activities/";
        public const string ApiUrlLists = ApiUrlBase + "lists/";
        public const string ApiUrlConversation = ApiUrlBase + "conversation/";
        public const string ApiUrlConversationKiss = ApiUrlConversation + "kiss/";
        public const string ApiUrlConversationHug = ApiUrlConversation + "hug/";
        public const string ApiUrlCalendar = ApiUrlBase + "calendar/";
        public const string ApiUrlMedia = ApiUrlBase + "media/";

        /// <summary>
        ///     The maximum amount of requests per day. By default this is set on 10k, as per
        ///     https://avocado.io/guacamole/avocado-api#api-throttle-limits.
        /// </summary>
        public static int MaxRateLimit = 10000;

        /// <summary>
        ///     Everytime a request is made and <see cref="EnableRateLimiting" /> is set to true, the library will update
        ///     <see cref="CurrentRate" />.
        /// </summary>
        public static int CurrentRate;

        /// <summary>
        ///     The Developer ID provided to you by Avocado.
        /// </summary>
        private readonly string _devId;

        /// <summary>
        ///     The Developer Key provided to you by Avocado.
        /// </summary>
        private readonly string _devKey;

        /// <summary>
        ///     Underlying
        /// </summary>
        private readonly HttpClient _httpClient;

        /// <summary>
        ///     Cookie Name
        /// </summary>
        private string _cookieName = "user_email";

        /// <summary>
        ///     The Cookie value returned by Avocado after a successful request.
        /// </summary>
        private string _cookieValue;

        /// <summary>
        ///     The Email that the user/client uses to autheticate itself with the Avocado API.
        /// </summary>
        private string _email;

        /// <summary>
        ///     A SHA256 hashed string that indicates that the intial authetication with Avocado was successful, every request with
        ///     the Avocado API requires this value to be present during the request.
        /// </summary>
        private string _signature;

        /// <summary>
        ///     Enable whether rate limiting should be used.
        ///     If <see cref="EnableRateLimiting" /> is false, no <see cref="RateLimitException" />s will be thrown.
        /// </summary>
        /// <remarks>
        ///     Even though <see cref="EnableRateLimiting" /> is set to false, Avocado still rate limit incoming requests on
        ///     their servers.
        /// </remarks>
        public bool EnableRateLimiting = false;

        /// <summary>
        ///     Create a new instance of the <see cref="NAvocadoClient" /> class, this will handle all communication between the
        ///     application and Avocado API endpoint.
        /// </summary>
        /// <param name="devId">Avocado Developer ID</param>
        /// <param name="devKey">Avocado Developer Key</param>
        public NAvocadoClient(string devId, string devKey)
        {
            _devId = devId;
            _devKey = devKey;

            _httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        }

        /// <summary>
        ///     Autheticate the <see cref="NAvocadoClient" /> with the Avocado API using the email and password provided by the
        ///     user.
        /// </summary>
        /// <param name="email">Email provided</param>
        /// <param name="password"></param>
        /// <returns>True if authentication was successful; otherwise false</returns>
        /// <exception cref="RateLimitException"></exception>
        /// <exception cref="AuthenticationFailedException"></exception>
        public async Task<bool> Login(string email, SecureString password)
        {
            // NOTE: Password is no longer 'secure' after calling ConvertToUnsecureString() (seems obvious), it is visible in memory and therefor a better solution will be required
            return await Login(email, password.ConvertToUnsecureString());
        }

        /// <summary>
        ///     Autheticate the <see cref="NAvocadoClient" /> with the Avocado API using the email and password provided by the
        ///     user.
        /// </summary>
        /// <param name="email">Email provided</param>
        /// <param name="password"></param>
        /// <returns>True if authentication was successful; otherwise false</returns>
        /// <remarks>
        ///     The authentication code is based on
        ///     https://github.com/xdumaine/Avocado/blob/master/Avocado/Models/AuthClient.cs.
        /// </remarks>
        /// <exception cref="RateLimitException"></exception>
        /// <exception cref="AuthenticationFailedException"></exception>
        public async Task<bool> Login(string email, string password)
        {
            await CheckRateLimit();

            _email = email;

            var credentials = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("email", email),
                new KeyValuePair<string, string>("password", password)
            });

            using (var response = await _httpClient.PostAsync(ApiUrlLogin, credentials))
            {
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    throw new BadRequestException("Bad username or password");
                }

                await IncrementCurrentRate(1);

                response.EnsureSuccessStatusCode();

                var tempCookie = response.Headers.First(x => x.Key == "Set-Cookie").Value.First();

                _cookieValue = tempCookie.Substring(tempCookie.IndexOf("=") + 1,
                    tempCookie.IndexOf(";") - tempCookie.IndexOf("=") - 1);

                var hashedCookie = (_cookieValue + _devKey).ToSHA256();
                _signature = _devId + ":" + hashedCookie;

                _cookieName = tempCookie;

                _httpClient.DefaultRequestHeaders.Add("X-AvoSig", _signature);


                return response.StatusCode == HttpStatusCode.OK && response.Headers.Contains("Set-Cookie");
            }
            // }
        }

        /// <summary>
        /// Log the current <see cref="NAvocadoUser"/> out
        /// </summary>
        /// <returns>True if successful; otherwise false</returns>
        public async Task<bool> Logout()
        {
            return await GetNothingAsync(ApiUrlLogout);
        }

        /// <summary>
        ///     Retrieve the <see cref="NAvocadoUser" /> that is associated with the provided Email
        /// </summary>
        /// <returns><see cref="NAvocadoUser" /> object</returns>
        /// <exception cref="RateLimitException"></exception>
        public async Task<NAvocadoUser> CurrentUser()
        {
            await CheckRateLimit();

            using (var response = await _httpClient.GetAsync(ApiUrlUser))
            {
                await IncrementCurrentRate(1);

                response.EnsureSuccessStatusCode();

                return
                    new JavaScriptSerializer().Deserialize<NAvocadoUser[]>(await response.Content.ReadAsStringAsync())[0
                        ];
            }
        }

        /// <summary>
        ///     Find a <see cref="NAvocadoUser" /> with the provided id.
        /// </summary>
        /// <param name="id">Id of the <see cref="NAvocadoUser" /> you want to find</param>
        /// <returns><see cref="NAvocadoUser" /> object</returns>
        /// <exception cref="RateLimitException"></exception>
        /// <exception cref="UserNotFoundException"></exception>
        public async Task<NAvocadoUser> User(string id)
        {
            await CheckRateLimit();

            using (var response = await _httpClient.GetAsync(ApiUrlUser + id))
            {
                await IncrementCurrentRate(1);

                response.EnsureSuccessStatusCode();

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new UserNotFoundException("User not found or inaccessible to you");
                }

                return
                    new JavaScriptSerializer().Deserialize<NAvocadoUser[]>(await response.Content.ReadAsStringAsync())[0
                        ];
            }
        }

        /// <summary>
        ///     Retrieve the <see cref="NAvocadoCouple" /> the <see cref="NAvocadoUser" /> is in
        /// </summary>
        /// <returns><see cref="NAvocadoCouple" /> object, containing both <see cref="NAvocadoUser" />s</returns>
        /// <remarks>
        ///     It might be smart if you immediatly call this instead of <see cref="GetCurrentUser" />, this will save a
        ///     call to the Avocado servers
        /// </remarks>
        public async Task<NAvocadoCouple> Couple()
        {
            return await SpecialMethodForCoupleObjectBecauseWtf<NAvocadoCouple>(ApiUrlCouple);
        }

        /// <summary>
        ///     Get the last 100 activities
        /// </summary>
        /// <returns>Array of <see cref="NAvocadoActivity" /></returns>
        /// <remarks>If you want activities before the last 100, use <see cref="ActivitiesBefore" /></remarks>
        public async Task<NAvocadoActivity[]> Activities()
        {
            await CheckRateLimit();

            using (var response = await _httpClient.GetAsync(ApiUrlActivities))
            {
                await IncrementCurrentRate(1);

                response.EnsureSuccessStatusCode();

                return
                    new JavaScriptSerializer().Deserialize<NAvocadoActivity[]>(
                        await response.Content.ReadAsStringAsync());
            }
        }

        /// <summary>
        ///     Get the last 100 activities and filter out everything but the required type.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public async Task<NAvocadoActivity[]> ActivitiesByType(NAvocadoActivityType type)
        {
            var a = await Activities();

            switch (type)
            {
                case NAvocadoActivityType.Message:
                    return a.ToList().FindAll(i => i.Type == "message").ToArray();
                case NAvocadoActivityType.Kiss:
                    return a.ToList().FindAll(i => i.Type == "kiss").ToArray();
                case NAvocadoActivityType.Hug:
                    return a.ToList().FindAll(i => i.Type == "hug").ToArray();
                case NAvocadoActivityType.List:
                    return a.ToList().FindAll(i => i.Type == "list").ToArray();
                case NAvocadoActivityType.Photo:
                    return a.ToList().FindAll(i => i.Type == "photo").ToArray();
                case NAvocadoActivityType.Media:
                    return a.ToList().FindAll(i => i.Type == "media").ToArray();
                case NAvocadoActivityType.Activity:
                    return a.ToList().FindAll(i => i.Type == "activity").ToArray();
                case NAvocadoActivityType.Couple:
                    return a.ToList().FindAll(i => i.Type == "couple").ToArray();
                case NAvocadoActivityType.User:
                    return a.ToList().FindAll(i => i.Type == "user").ToArray();
                default:
                    return a;
            }
        }

        /// <summary>
        ///     Get all activities <b>after</b> a certain time.
        /// </summary>
        /// <param name="time"><see cref="DateTime" /> representing</param>
        /// <returns></returns>
        public async Task<NAvocadoActivity[]> ActivitiesAfter(DateTime time)
        {
            return await GetArrayAsync<NAvocadoActivity>(ApiUrlActivities + "?after=" + time.ToUnixTimestampAsLong());
        }

        /// <summary>
        ///     Get all activities <b>before</b> a certain time.
        /// </summary>
        /// <param name="time"></param>
        /// <returns></returns>
        public async Task<NAvocadoActivity[]> ActivitiesBefore(DateTime time)
        {
            return await GetArrayAsync<NAvocadoActivity>(ApiUrlActivities + "?before=" + time.ToUnixTimestampAsLong());
        }

        /// <summary>
        /// Send a message to the Avocado API and the other use in the <see cref="NAvocadoCouple"/>.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public async Task<bool> Message(string message)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("message", message)
            });

            return await PostAsync(ApiUrlConversation, content);
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public async Task<bool> Hug()
        {
            return await PostAsync(ApiUrlConversationHug, null);
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public async Task<NAvocadoList[]> Lists()
        {
            return await GetArrayAsync<NAvocadoList>(ApiUrlLists);
        }

        /// <summary>
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<NAvocadoList> List(string id)
        {
            return await GetSingleAsync<NAvocadoList>(ApiUrlLists + id);
        }

        /// <summary>
        /// </summary>
        /// <param name="listName"></param>
        /// <returns></returns>
        public async Task<bool> CreateList(string listName)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("name", listName)
            });

            return await PostAsync(ApiUrlLists, content);
        }

        /// <summary>
        /// </summary>
        /// <param name="id"></param>
        /// <param name="newName"></param>
        /// <returns></returns>
        public async Task<bool> RenameList(string id, string newName)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("name", newName)
            });

            return await PostAsync(ApiUrlLists + id, content);
        }

        /// <summary>
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<bool> DeleteList(string id)
        {
            return await PostAsync(ApiUrlLists + id + "/delete", null);
        }

        #region Private Areas ( ͡° ͜ʖ ͡°)
        private async Task IncrementCurrentRate(int incrementBy)
        {
            await Task.Factory.StartNew(() =>
            {
                if (EnableRateLimiting)
                {
                    CurrentRate = CurrentRate + incrementBy;
                }
            });
        }

        /// <summary>
        /// Check if the rate limit ihas been exceeded and throw an exception to notify the application, <see cref="EnableRateLimiting"/> should be true.
        /// </summary>
        /// <returns>Nothing; otherwise an error</returns>
        private async Task CheckRateLimit()
        {
            await Task.Factory.StartNew(() =>
            {
                if (EnableRateLimiting && CurrentRate >= MaxRateLimit)
                {
                    throw new RateLimitException("CurrentRate exceeds MaxRateLimit");
                }
            });
        }

        /// <summary>
        /// <see cref="GetSingleAsync{T}"/> will not work for <see cref="NAvocadoCouple"/>, so this mthod is dedicated for the special ones.
        /// Everything is returned as array except <see cref="NAvocadoCouple"/>, why is this even a thing?
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private async Task<T> SpecialMethodForCoupleObjectBecauseWtf<T>(string url)
        {
            await CheckRateLimit();

            using (var response = await _httpClient.GetAsync(url))
            {
                await IncrementCurrentRate(1);

                response.EnsureSuccessStatusCode();

                return new JavaScriptSerializer().Deserialize<T>(await response.Content.ReadAsStringAsync());
            }
        }

        /// <summary>
        /// Get nothing async? Por que? What is this sorcery?
        /// </summary>
        /// <param name="url"></param>
        /// <returns>True if we got nothing!; otherwise... false???</returns>
        private async Task<bool> GetNothingAsync(string url)
        {
            await CheckRateLimit();

            using (var response = await _httpClient.GetAsync(url))
            {
                await IncrementCurrentRate(1);

                response.EnsureSuccessStatusCode();

                return response.StatusCode == HttpStatusCode.OK;
            }
        }

        /// <summary>
        ///    Get data, as a sinlge instance type thingy object stuff? ASYNC?!
        /// </summary>
        /// <typeparam name="T">The type the object should be of (english?)</typeparam>
        /// <param name="url"></param>
        /// <returns></returns>
        private async Task<T> GetSingleAsync<T>(string url)
        {
            await CheckRateLimit();

            using (var response = await _httpClient.GetAsync(url))
            {
                await IncrementCurrentRate(1);

                response.EnsureSuccessStatusCode();

                return new JavaScriptSerializer().Deserialize<T[]>(await response.Content.ReadAsStringAsync())[0];
            }
        }

        /// <summary>
        /// Get data, as an array and it's ASYNC!
        /// </summary>
        /// <typeparam name="T">The object type the array should be of (english?)</typeparam>
        /// <param name="url"></param>
        /// <returns></returns>
        private async Task<T[]> GetArrayAsync<T>(string url)
        {
            await CheckRateLimit();

            using (var response = await _httpClient.GetAsync(url))
            {
                await IncrementCurrentRate(1);

                response.EnsureSuccessStatusCode();

                return new JavaScriptSerializer().Deserialize<T[]>(await response.Content.ReadAsStringAsync());
            }
        }

        /// <summary>
        /// Post data, ASYNC! FUCK YEAH!
        /// </summary>
        /// <param name="url">The url to post to</param>
        /// <param name="content">The content to post</param>
        /// <returns>True if response is 200; otherwise false</returns>
        private async Task<bool> PostAsync(string url, FormUrlEncodedContent content)
        {
            using (var response = await _httpClient.PostAsync(url, content))
            {
                response.EnsureSuccessStatusCode();

                return response.StatusCode == HttpStatusCode.OK;
            }
        }
        #endregion
    }
}