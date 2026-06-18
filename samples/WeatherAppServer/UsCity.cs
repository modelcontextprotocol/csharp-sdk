using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<UsCity>))]
public enum UsCity
{
    [JsonStringEnumMemberName("Albuquerque, NM")] AlbuquerqueNM,
    [JsonStringEnumMemberName("Atlanta, GA")] AtlantaGA,
    [JsonStringEnumMemberName("Austin, TX")] AustinTX,
    [JsonStringEnumMemberName("Boston, MA")] BostonMA,
    [JsonStringEnumMemberName("Charlotte, NC")] CharlotteNC,
    [JsonStringEnumMemberName("Chicago, IL")] ChicagoIL,
    [JsonStringEnumMemberName("Dallas, TX")] DallasTX,
    [JsonStringEnumMemberName("Denver, CO")] DenverCO,
    [JsonStringEnumMemberName("Houston, TX")] HoustonTX,
    [JsonStringEnumMemberName("Indianapolis, IN")] IndianapolisIN,
    [JsonStringEnumMemberName("Las Vegas, NV")] LasVegasNV,
    [JsonStringEnumMemberName("Los Angeles, CA")] LosAngelesCA,
    [JsonStringEnumMemberName("Miami, FL")] MiamiFL,
    [JsonStringEnumMemberName("Minneapolis, MN")] MinneapolisMN,
    [JsonStringEnumMemberName("Nashville, TN")] NashvilleTN,
    [JsonStringEnumMemberName("New York, NY")] NewYorkNY,
    [JsonStringEnumMemberName("Orlando, FL")] OrlandoFL,
    [JsonStringEnumMemberName("Philadelphia, PA")] PhiladelphiaPA,
    [JsonStringEnumMemberName("Phoenix, AZ")] PhoenixAZ,
    [JsonStringEnumMemberName("Portland, OR")] PortlandOR,
    [JsonStringEnumMemberName("Salt Lake City, UT")] SaltLakeCityUT,
    [JsonStringEnumMemberName("San Diego, CA")] SanDiegoCA,
    [JsonStringEnumMemberName("San Francisco, CA")] SanFranciscoCA,
    [JsonStringEnumMemberName("Seattle, WA")] SeattleWA,
    [JsonStringEnumMemberName("Washington, DC")] WashingtonDC,
}

public static class UsCityData
{
    public static (double Latitude, double Longitude) GetCoordinates(UsCity city) => city switch
    {
        UsCity.AlbuquerqueNM => (35.0844, -106.6504),
        UsCity.AtlantaGA => (33.7490, -84.3880),
        UsCity.AustinTX => (30.2672, -97.7431),
        UsCity.BostonMA => (42.3601, -71.0589),
        UsCity.CharlotteNC => (35.2271, -80.8431),
        UsCity.ChicagoIL => (41.8781, -87.6298),
        UsCity.DallasTX => (32.7767, -96.7970),
        UsCity.DenverCO => (39.7392, -104.9903),
        UsCity.HoustonTX => (29.7604, -95.3698),
        UsCity.IndianapolisIN => (39.7684, -86.1581),
        UsCity.LasVegasNV => (36.1699, -115.1398),
        UsCity.LosAngelesCA => (34.0522, -118.2437),
        UsCity.MiamiFL => (25.7617, -80.1918),
        UsCity.MinneapolisMN => (44.9778, -93.2650),
        UsCity.NashvilleTN => (36.1627, -86.7816),
        UsCity.NewYorkNY => (40.7128, -74.0060),
        UsCity.OrlandoFL => (28.5383, -81.3792),
        UsCity.PhiladelphiaPA => (39.9526, -75.1652),
        UsCity.PhoenixAZ => (33.4484, -112.0740),
        UsCity.PortlandOR => (45.5152, -122.6784),
        UsCity.SaltLakeCityUT => (40.7608, -111.8910),
        UsCity.SanDiegoCA => (32.7157, -117.1611),
        UsCity.SanFranciscoCA => (37.7749, -122.4194),
        UsCity.SeattleWA => (47.6062, -122.3321),
        UsCity.WashingtonDC => (38.9072, -77.0369),
        _ => throw new ArgumentOutOfRangeException(nameof(city))
    };
}
