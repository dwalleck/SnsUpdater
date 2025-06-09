using System;
using MediatR;

namespace SnsUpdater.API.Events
{
    public class PersonCreatedEvent : INotification
    {
        public int PersonId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string PhoneNumber { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}