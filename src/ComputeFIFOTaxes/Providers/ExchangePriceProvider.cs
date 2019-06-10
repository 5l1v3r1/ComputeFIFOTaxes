﻿using ComputeFIFOTaxes.Helpers;
using ComputeFIFOTaxes.Interfaces;
using ComputeFIFOTaxes.Models;
using ComputeFIFOTaxes.Parsers;
using ComputeFIFOTaxes.Types;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ComputeFIFOTaxes.Providers
{
    public class ExchangePriceProvider : FiatProviderBase
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="config">Config</param>
        public ExchangePriceProvider(Config.FiatProviderConfig config) : base(config.FiatCoin) { }

        /// <summary>
        /// Get fiat price for one coin in specific date
        /// </summary>
        /// <param name="parser">Parser</param>
        /// <param name="coin">Coin</param>
        /// <param name="date">Date</param>
        /// <returns>Price</returns>
        protected override decimal InternalGetFiatPrice(ITradeParser parser, ECoin coin, DateTime date)
        {
            switch (parser)
            {
                // https://www.kraken.com/features/api#get-ohlc-data

                case null:
                case KrakenTradesParser _:
                    {
                        var minV = 0M;
                        var maxV = 0M;

                        foreach (var path in GetKrakenPathToCoin(coin))
                        {
                            var sience = GetKrakenTicks(path, date);

                            if (sience == null) throw new ArgumentException(nameof(sience));

                            var min = sience.Select(u => u.Low).Min();
                            var max = sience.Select(u => u.High).Max();

                            if (minV == 0)
                            {
                                minV = min;
                                maxV = max;
                            }
                            else
                            {
                                minV *= min;
                                maxV *= max;
                            }
                        }

                        return (minV + maxV) / 2M;
                    }

                //https://github.com/binance-exchange/binance-official-api-docs/blob/master/rest-api.md

                case BinanceParser _:
                    {
                        var time = (long)(date.AddSeconds(-date.Second)).ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

                        // Parse to btc

                        var minV = 0M;
                        var maxV = 0M;
                        var avgV = 0M;

                        foreach (var path in GetBinancePathToBtc(coin))
                        {
                            var arr = DownloadHelper.Download<JArray[]>($"https://api.binance.com/api/v1/klines?symbol={path}&interval=1m&startTime={time}&endTime={time + 60000}");

                            if (arr.Length == 0) throw new ArgumentException(nameof(coin));

                            var min = 0M;
                            var max = 0M;
                            var avg = 0M;

                            foreach (JArray entry in arr)
                            {
                                max += entry[2].Value<decimal>();
                                min += entry[3].Value<decimal>();
                                avg += entry[4].Value<decimal>();
                            }

                            min /= arr.Length;
                            max /= arr.Length;
                            avg /= arr.Length;

                            if (minV == 0)
                            {
                                minV = min;
                                maxV = max;
                                avgV = avg;
                            }
                            else
                            {
                                minV *= min;
                                maxV *= max;
                                avgV *= avg;
                            }
                        }

                        // Kraken price for bitcoin

                        return GetFiatPrice(null, ECoin.BTC, date) * avgV;
                    }

                default: throw new ArgumentException(nameof(parser));
            }
        }

        private KrakenOHLC[] GetKrakenTicks(string path, DateTime date)
        {
            var time = (long)(date).ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

            var sience = 0L;
            var interval = 1440;
            KrakenOHLC first = null, last = null;

        START:

            var arr = DownloadHelper.Download<KrakenResult<TimestampedDictionary<string, KrakenOHLC[]>>>(
                "https://api.kraken.com/0/public/OHLC?" + DownloadHelper.UrlEncode(new Dictionary<string, string>(3)
                {
                    ["pair"] = path,
                    ["interval"] = interval.ToString(),
                    ["since"] = sience.ToString()
                }));

            if (arr.Result == null) throw new ArgumentException();

            foreach (var entry in arr.Result.Values)
            {
                foreach (var row in entry)
                {
                    if (row.Time > time)
                    {
                        last = row;
                        break;
                    }

                    first = row;
                    sience = row.Time;
                }
            }

            if (last != null)
            {
                if (first == null) throw new ArgumentException();

                return new KrakenOHLC[] { first, last };
            }
            else
            {
                goto START;
            }
        }

        /// <summary>
        /// Get Binance path
        /// </summary>
        /// <param name="coin">Coin</param>
        /// <returns>Path</returns>
        public IEnumerable<string> GetBinancePathToBtc(ECoin coin)
        {
            switch (coin)
            {
                case ECoin.BTC: return new string[] { };

                default: return new string[] { coin.ToString() + "BTC" };
            }
        }

        /// <summary>
        /// Get Kraken path
        /// </summary>
        /// <param name="coin">Coin</param>
        /// <returns>Path</returns>
        private IEnumerable<string> GetKrakenPathToCoin(ECoin coin)
        {
            if (Coin == coin) return new string[] { };

            switch (coin)
            {
                case ECoin.EOS:
                case ECoin.BTC: return new string[] { "XBT" + Coin.ToString() };
            }

            throw new ArgumentException(nameof(coin));
        }
    }
}