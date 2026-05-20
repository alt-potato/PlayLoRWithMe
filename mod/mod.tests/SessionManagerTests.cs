using System.Collections.Generic;
using PlayLoRWithMe;
using Xunit;

namespace PlayLoRWithMe.Tests
{
    /// <summary>
    /// Coverage for <see cref="SessionManager"/> claim/release, authorization,
    /// exclusive librarian locking, unit-id translation, rename, and player-list
    /// serialization. Sessions are created without a live WebSocketClient; since
    /// none are marked connected, broadcast helpers are no-ops here.
    /// </summary>
    public class SessionManagerTests
    {
        [Fact]
        public void ClaimGrantsAuthorization_ReleaseRevokesIt()
        {
            var sm = new SessionManager();
            var session = sm.GetOrCreate(null);

            Assert.True(sm.ClaimUnit(session.SessionId, 7));
            Assert.True(sm.IsAuthorized(session.SessionId, 7));

            sm.ReleaseUnit(session.SessionId, 7);
            Assert.False(sm.IsAuthorized(session.SessionId, 7));
        }

        [Fact]
        public void ClaimedUnit_CannotBeClaimedOrUsedByAnotherSession()
        {
            var sm = new SessionManager();
            var owner = sm.GetOrCreate(null);
            var other = sm.GetOrCreate(null);

            Assert.True(sm.ClaimUnit(owner.SessionId, 3));

            Assert.False(sm.ClaimUnit(other.SessionId, 3));
            Assert.False(sm.IsAuthorized(other.SessionId, 3));
        }

        [Fact]
        public void LibrarianLock_IsExclusiveToItsHolder()
        {
            var sm = new SessionManager();
            var first = sm.GetOrCreate(null);
            var second = sm.GetOrCreate(null);

            Assert.True(sm.TryLockLibrarian("0:1", first.SessionId));
            Assert.False(sm.TryLockLibrarian("0:1", second.SessionId));

            Assert.True(sm.IsLibrarianLockHolder("0:1", first.SessionId));
            Assert.False(sm.IsLibrarianLockHolder("0:1", second.SessionId));
            Assert.Equal(first.DisplayName, sm.GetLibrarianLockerName("0:1"));
        }

        [Fact]
        public void TranslateUnitIds_RemapsExistingClaims()
        {
            var sm = new SessionManager();
            var session = sm.GetOrCreate(null);
            sm.ClaimUnit(session.SessionId, 0);
            sm.ClaimUnit(session.SessionId, 1);

            sm.TranslateUnitIds(new Dictionary<int, int> { { 0, 100 }, { 1, 101 } });

            Assert.True(sm.IsAuthorized(session.SessionId, 100));
            Assert.True(sm.IsAuthorized(session.SessionId, 101));
            // Old position-indices no longer authorize.
            Assert.False(sm.IsAuthorized(session.SessionId, 0));
            Assert.False(sm.IsAuthorized(session.SessionId, 1));
        }

        [Fact]
        public void RenameSession_AndPlayerListJson_ReflectNameAndUnits()
        {
            var sm = new SessionManager();
            var session = sm.GetOrCreate(null);
            sm.ClaimUnit(session.SessionId, 4);
            sm.RenameSession(session.SessionId, "Alice");

            string json = sm.BuildPlayerListJson();

            Assert.Contains("\"type\":\"playerList\"", json);
            Assert.Contains("\"sessionId\":\"" + session.SessionId + "\"", json);
            Assert.Contains("\"name\":\"Alice\"", json);
            Assert.Contains("\"units\":[4]", json);
        }
    }
}
