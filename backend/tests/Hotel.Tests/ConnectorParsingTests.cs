using System.Text.Json;
using System.Xml.Linq;
using Hotel.Application.Channels;
using Hotel.Domain.Bookings;
using Hotel.Infrastructure.Channels;
using Hotel.Infrastructure.Channels.BookingCom;
using Hotel.Infrastructure.Channels.Channex;
using Hotel.Infrastructure.Channels.Expedia;
using Hotel.Infrastructure.Channels.ICal;

namespace Hotel.Tests;

public class ConnectorParsingTests
{
    [Fact]
    public void Compress_merges_consecutive_equal_days()
    {
        var d = new DateOnly(2026, 1, 1);
        var ranges = AriRanges.Compress([(d, 5), (d.AddDays(1), 5), (d.AddDays(2), 3), (d.AddDays(4), 3)]).ToList();
        Assert.Equal(3, ranges.Count);
        Assert.Equal((d, d.AddDays(1), 5), (ranges[0].From, ranges[0].ToInclusive, ranges[0].Value));
        Assert.Equal(d.AddDays(2), ranges[1].ToInclusive); // gap on day 3 splits the range
    }

    [Fact]
    public void BookingCom_reservation_xml_is_parsed()
    {
        var xml = XDocument.Parse("""
            <OTA_HotelResNotifRQ xmlns="http://www.opentravel.org/OTA/2003/05" Version="2.001">
              <HotelReservations>
                <HotelReservation ResStatus="Commit">
                  <RoomStays>
                    <RoomStay>
                      <RoomTypes><RoomType RoomTypeCode="1234501"/></RoomTypes>
                      <RatePlans><RatePlan RatePlanCode="777"/></RatePlans>
                      <GuestCounts><GuestCount AgeQualifyingCode="10" Count="2"/><GuestCount AgeQualifyingCode="8" Count="1"/></GuestCounts>
                      <TimeSpan Start="2026-11-01" End="2026-11-04"/>
                      <Total AmountAfterTax="450.00" CurrencyCode="EUR"/>
                    </RoomStay>
                  </RoomStays>
                  <ResGuests><ResGuest><Profiles><ProfileInfo><Profile><Customer>
                    <PersonName><GivenName>Jane</GivenName><Surname>Doe</Surname></PersonName>
                    <Telephone PhoneNumber="+441234"/><Email>jane@guest.booking.com</Email>
                    <Address><CountryName Code="GB"/></Address>
                  </Customer></Profile></ProfileInfo></Profiles></ResGuest></ResGuests>
                  <ResGlobalInfo><HotelReservationIDs><HotelReservationID ResID_Value="4105732951"/></HotelReservationIDs></ResGlobalInfo>
                </HotelReservation>
              </HotelReservations>
            </OTA_HotelResNotifRQ>
            """);

        var r = Assert.Single(BookingComConnector.ParseReservations(xml));
        Assert.Equal("4105732951", r.ExternalId);
        Assert.Equal(ExternalReservationStatus.New, r.Status);
        Assert.Equal("1234501", r.ExternalRoomId);
        Assert.Equal("777", r.ExternalRateId);
        Assert.Equal((2, 1), (r.Adults, r.Children));
        Assert.Equal(new DateOnly(2026, 11, 4), r.CheckOut);
        Assert.Equal(450m, r.TotalAmount);
        Assert.Equal("Jane", r.Guest.FirstName);
        Assert.Equal("GB", r.Guest.Country);
    }

    [Fact]
    public void Expedia_booking_retrieval_is_parsed()
    {
        var xml = XDocument.Parse("""
            <BookingRetrievalRS xmlns="http://www.expediaconnect.com/EQC/BR/2014/01">
              <Bookings>
                <Booking id="98765" type="Modify" createDateTime="2026-10-01T10:00:00Z" source="Expedia" status="pending">
                  <Hotel id="111"/>
                  <RoomStay roomTypeID="200" ratePlanID="300">
                    <StayDate arrival="2026-12-10" departure="2026-12-12"/>
                    <GuestCount adult="2" child="0"/>
                    <Total amountAfterTaxes="320.50" currency="USD"/>
                  </RoomStay>
                  <PrimaryGuest><Name givenName="Max" surname="Mustermann"/><Email>max@example.com</Email></PrimaryGuest>
                  <SpecialRequest code="1.14">Late arrival</SpecialRequest>
                </Booking>
              </Bookings>
            </BookingRetrievalRS>
            """);

        var r = Assert.Single(ExpediaConnector.ParseBookings(xml));
        Assert.Equal(ExternalReservationStatus.Modified, r.Status);
        Assert.Equal(("200", "300"), (r.ExternalRoomId, r.ExternalRateId));
        Assert.Equal(320.50m, r.TotalAmount);
        Assert.Equal("Late arrival", r.Notes);
        Assert.Equal("Modify", r.AckToken);
    }

    [Fact]
    public void Channex_revision_feed_is_mapped_to_source_ota()
    {
        var json = """
            {"data":[{"id":"rev-1","type":"booking_revision","attributes":{
              "booking_id":"bk-1","status":"new","ota_name":"BookingCom","ota_reservation_code":"123",
              "arrival_date":"2026-11-01","departure_date":"2026-11-03","amount":"300.00","currency":"EUR",
              "payment_collect":"ota","notes":"Quiet room",
              "customer":{"name":"Ana","surname":"Silva","mail":"ana@example.com","phone":"+351","country":"PT"},
              "rooms":[{"room_type_id":"rt-1","rate_plan_id":"rp-1","checkin_date":"2026-11-01","checkout_date":"2026-11-03",
                        "amount":"300.00","occupancy":{"adults":2,"children":1,"infants":0}}]}}]}
            """;
        var feed = JsonSerializer.Deserialize<ChannexFeed>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        var r = Assert.Single(ChannexConnector.Map(feed));
        Assert.Equal("bk-1", r.ExternalId);
        Assert.Equal("rev-1", r.AckToken);
        Assert.Equal(BookingSource.BookingCom, r.Source);
        Assert.True(r.PaidToChannel);
        Assert.Equal(1, r.Children);
        Assert.Equal("rt-1", r.ExternalRoomId);
    }

    [Fact]
    public void ICal_roundtrip_and_folded_lines()
    {
        var ics = ICalendar.Write("Test", "Room", [new ICalEvent("a@x", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3), "Blocked, sorry", false)]);
        var parsed = Assert.Single(ICalendar.Parse(ics));
        Assert.Equal(new DateOnly(2026, 5, 3), parsed.End);

        const string airbnb = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20260601\r\nDTEND;VALUE=DATE:20260605\r\nUID:1418fb94e984-\r\n 7a5c@airbnb.com\r\nSUMMARY:Reserved\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var e = Assert.Single(ICalendar.Parse(airbnb));
        Assert.Equal("1418fb94e984-7a5c@airbnb.com", e.Uid);
        Assert.Equal(4, e.End.DayNumber - e.Start.DayNumber);
    }
}
