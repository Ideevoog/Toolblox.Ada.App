using Newtonsoft.Json;
using System;
using System.Numerics;

namespace Toolblox.Ada.App.Functions
{
    public class Invoice : EntityBase
    {
        public string Contract { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Article { get; set; }
        public string AmountString { get; set; }
        public BigInteger? Amount { get; set; }
        public string Currency { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ModifiedAt { get; set; }
    }

    public class Accountant : EntityBase
    {
        public string? User { get; set; }
    }

    public class EntityBase
    {
        public event Action OnUpdate;

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public void StateHasChanged() => OnUpdate?.Invoke();
    }
}
