using System.Text.RegularExpressions;

namespace CounterTalk
{
    public static class SpeechPolicy
    {
        // Conservative detection of explicit priced acceptance, not a general semantic classifier.
        public static bool PricedAcceptance(string speech) => !string.IsNullOrWhiteSpace(speech) &&
            Regex.IsMatch(speech, @"\d|[一二三四五六七八九十百千]+(?:块|信用点|就)") &&
            Regex.IsMatch(speech, @"认了|说定了|就这么定|成交|就按|(?:[零一二三四五六七八九十百千\d]+)就(?:[零一二三四五六七八九十百千\d]+)|\bagreed\b|\bdeal\b", RegexOptions.IgnoreCase);
    }
}
