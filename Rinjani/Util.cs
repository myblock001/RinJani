using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Timers;
using Jose;

namespace Rinjani
{
    public static class Util
    {
        public static IList<IBrokerAdapter> GetEnabledBrokerAdapters(IList<IBrokerAdapter> brokerAdapters,
            IConfigStore configStore)
        {
            var enabledBrokers = configStore.Config.Brokers.Where(b => b.Enabled).Select(b => b.Broker).ToList();
            return brokerAdapters.Where(b => enabledBrokers.Contains(b.Broker)).ToList();
        }

        public static string Nonce => Convert.ToString(DateTime.UtcNow.ToUnixMs());

        public static DateTime IsoDateTimeToLocal(string isoTime)
        {
            return DateTimeOffset.Parse(isoTime, null, DateTimeStyles.RoundtripKind).LocalDateTime;
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            var dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        public static string Hr(int width)
        {
            return string.Concat(Enumerable.Repeat("-", width));
        }

        public static void StartTimer(ITimer timer, double interval, ElapsedEventHandler handler)
        {
            timer.Interval = interval;
            timer.Elapsed += handler;
            timer.Start();
        }

        public static long ToUnixMs(this DateTime dt)
        {
            return (long)dt.ToUniversalTime().Subtract(
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            ).TotalMilliseconds;
        }

        public static string GenerateNewHmac(string secret, string message)
        {
            var hmc = new HMACSHA256(Encoding.ASCII.GetBytes(secret));
            var hmres = hmc.ComputeHash(Encoding.ASCII.GetBytes(message));
            return BitConverter.ToString(hmres).Replace("-", "").ToLower();
        }

        public static IEnumerable<Quote> Merge(this IEnumerable<Quote> quotes, decimal step)
        {
            return quotes.ToLookup(q =>
                {
                    var price = q.Side == QuoteSide.Ask
                        ? Math.Ceiling(q.Price / step) * step
                        : Math.Floor(q.Price / step) * step;
                    return new QuoteKey(q.Broker, q.Side, price,q.BasePrice);
                })
                .Select(l => new Quote(l.Key.Broker, l.Key.Side, l.Key.Price, l.Key.BasePrice, l.Sum(q => q.Volume)));
        }

        public static T ParseEnum<T>(string value)
        {
            return (T)Enum.Parse(typeof(T), value, true);
        }

        public static string JwtHs256Encode(object payload, string secret)
        {
            var secbyte = Encoding.UTF8.GetBytes(secret);
            return JWT.Encode(payload, secbyte, JwsAlgorithm.HS256);
        }

        public static IEnumerable<T> Enums<T>()
        {
            return Enum.GetValues(typeof(T)).Cast<T>();
        }

        public static decimal RoundDown(decimal d, double decimalPlaces)
        {
            var power = Convert.ToDecimal(Math.Pow(10, decimalPlaces));
            return Math.Floor(d * power) / power;
        }

        private static String encodingCharset = "UTF-8";
        /**
       *
       * @param aValue  要加密的文字
       * @param aKey  密钥
       * @return
       */
        public static String hmacSign(String aValue, String aKey)
        {
            byte[] k_ipad = new byte[64];
            byte[] k_opad = new byte[64];
            byte[] keyb;
            byte[] value;
            Encoding coding = Encoding.GetEncoding(encodingCharset);
            try
            {
                keyb = coding.GetBytes(aKey);
                // aKey.getBytes(encodingCharset);
                value = coding.GetBytes(aValue);
                // aValue.getBytes(encodingCharset);
            }
            catch (Exception e)
            {
                keyb = null;
                value = null;
                //throw;
            }
            for (int i = keyb.Length; i < 64; i++)
            {
                k_ipad[i] = (byte)54;
                k_opad[i] = (byte)92;
            }
            for (int i = 0; i < keyb.Length; i++)
            {
                k_ipad[i] = (byte)(keyb[i] ^ 0x36);
                k_opad[i] = (byte)(keyb[i] ^ 0x5c);
            }
            byte[] sMd5_1 = MakeMD5(k_ipad.Concat(value).ToArray());
            byte[] dg = MakeMD5(k_opad.Concat(sMd5_1).ToArray());
            return toHex(dg);
        }
        public static String toHex(byte[] input)
        {
            if (input == null)
                return null;
            StringBuilder output = new StringBuilder(input.Length * 2);
            for (int i = 0; i < input.Length; i++)
            {
                int current = input[i] & 0xff;
                if (current < 16)
                    output.Append('0');
                output.Append(current.ToString("x"));
            }
            return output.ToString();
        }
        /**
         * 
         * @param args
         * @param key
         * @return
         */
        public static String getHmac(String[] args, String key)
        {
            if (args == null || args.Length == 0)
            {
                return (null);
            }
            StringBuilder str = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                str.Append(args[i]);
            }
            return (hmacSign(str.ToString(), key));
        }
        /// <summary>
        /// 生成MD5摘要
        /// </summary>
        /// <param name="original">数据源</param>
        /// <returns>摘要</returns>
        public static byte[] MakeMD5(byte[] original)
        {
            MD5CryptoServiceProvider hashmd5 = new MD5CryptoServiceProvider();
            byte[] keyhash = hashmd5.ComputeHash(original);
            hashmd5 = null;
            return keyhash;
        }
        /**
         * SHA加密
         * @param aValue
         * @return
         */
        public static String digest(String aValue)
        {
            aValue = aValue.Trim();
            byte[] value;
            SHA1 sha = null;
            Encoding coding = Encoding.GetEncoding(encodingCharset);
            try
            {
                value = coding.GetBytes(aValue);
                // aValue.getBytes(encodingCharset);
                HashAlgorithm ha = (HashAlgorithm)CryptoConfig.CreateFromName("SHA");
                value = ha.ComputeHash(value);
            }
            catch (Exception e)
            {
                //value = coding.GetBytes(aValue);
                throw;
            }
            return toHex(value);
        }
    }
}