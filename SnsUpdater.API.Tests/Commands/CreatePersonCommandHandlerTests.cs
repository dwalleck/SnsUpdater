using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MediatR;
using SnsUpdater.API.Commands;
using SnsUpdater.API.Events;

namespace SnsUpdater.API.Tests.Commands
{
    [TestClass]
    public class CreatePersonCommandHandlerTests
    {
        private Mock<IMediator> _mediatorMock;
        private CreatePersonCommandHandler _handler;

        [TestInitialize]
        public void Setup()
        {
            _mediatorMock = new Mock<IMediator>();
            _handler = new CreatePersonCommandHandler(_mediatorMock.Object);
        }

        [TestMethod]
        public async Task Handle_ValidPersonWithAllFields_ReturnsSuccessAndPublishesEvent()
        {
            // Arrange
            var command = new CreatePersonCommand
            {
                FirstName = "John",
                LastName = "Doe",
                PhoneNumber = "123-456-7890"
            };

            PersonCreatedEvent publishedEvent = null;
            _mediatorMock.Setup(m => m.Publish(It.IsAny<PersonCreatedEvent>(), It.IsAny<CancellationToken>()))
                .Callback<PersonCreatedEvent, CancellationToken>((evt, ct) => publishedEvent = evt)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.AreEqual("Person created successfully.", result.Message);
            Assert.AreEqual("John", result.FirstName);
            Assert.AreEqual("Doe", result.LastName);
            Assert.AreEqual("123-456-7890", result.PhoneNumber);
            Assert.IsTrue(result.Id >= 1000 && result.Id <= 9999);

            _mediatorMock.Verify(m => m.Publish(It.IsAny<PersonCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            
            Assert.IsNotNull(publishedEvent);
            Assert.AreEqual(result.Id, publishedEvent.PersonId);
            Assert.AreEqual("John", publishedEvent.FirstName);
            Assert.AreEqual("Doe", publishedEvent.LastName);
            Assert.AreEqual("123-456-7890", publishedEvent.PhoneNumber);
            Assert.IsTrue(Math.Abs((publishedEvent.CreatedAt - DateTime.UtcNow).TotalSeconds) < 5);
        }

        [TestMethod]
        public async Task Handle_ValidPersonWithoutPhoneNumber_ReturnsSuccessAndPublishesEvent()
        {
            // Arrange
            var command = new CreatePersonCommand
            {
                FirstName = "Jane",
                LastName = "Smith",
                PhoneNumber = null
            };

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.IsNull(result.PhoneNumber);
            _mediatorMock.Verify(m => m.Publish(It.IsAny<PersonCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task Handle_EmptyFirstName_ReturnsFailure()
        {
            // Arrange
            var command = new CreatePersonCommand
            {
                FirstName = "",
                LastName = "Doe",
                PhoneNumber = "123-456-7890"
            };

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("First name and last name are required.", result.Message);
            Assert.AreEqual(0, result.Id);
            _mediatorMock.Verify(m => m.Publish(It.IsAny<PersonCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestMethod]
        public async Task Handle_EmptyLastName_ReturnsFailure()
        {
            // Arrange
            var command = new CreatePersonCommand
            {
                FirstName = "John",
                LastName = "",
                PhoneNumber = "123-456-7890"
            };

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("First name and last name are required.", result.Message);
            _mediatorMock.Verify(m => m.Publish(It.IsAny<PersonCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestMethod]
        public async Task Handle_NullFirstName_ReturnsFailure()
        {
            // Arrange
            var command = new CreatePersonCommand
            {
                FirstName = null,
                LastName = "Doe",
                PhoneNumber = "123-456-7890"
            };

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("First name and last name are required.", result.Message);
            _mediatorMock.Verify(m => m.Publish(It.IsAny<PersonCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestMethod]
        public async Task Handle_WhitespaceOnlyFirstName_ReturnsFailure()
        {
            // Arrange
            var command = new CreatePersonCommand
            {
                FirstName = "   ",
                LastName = "Doe",
                PhoneNumber = "123-456-7890"
            };

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("First name and last name are required.", result.Message);
            _mediatorMock.Verify(m => m.Publish(It.IsAny<PersonCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestMethod]
        public async Task Handle_MediatorPublishThrowsException_ReturnsFailure()
        {
            // Arrange
            var command = new CreatePersonCommand
            {
                FirstName = "John",
                LastName = "Doe",
                PhoneNumber = "123-456-7890"
            };

            _mediatorMock.Setup(m => m.Publish(It.IsAny<PersonCreatedEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Publish failed"));

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "An error occurred while creating the person");
            StringAssert.Contains(result.Message, "Publish failed");
        }

        [TestMethod]
        public async Task Handle_CancellationRequested_ThrowsOperationCanceledException()
        {
            // Arrange
            var command = new CreatePersonCommand
            {
                FirstName = "John",
                LastName = "Doe",
                PhoneNumber = "123-456-7890"
            };

            var cts = new CancellationTokenSource();
            cts.Cancel();

            _mediatorMock.Setup(m => m.Publish(It.IsAny<PersonCreatedEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            // Act
            var result = await _handler.Handle(command, cts.Token);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "An error occurred while creating the person");
        }

        [TestMethod]
        public async Task Handle_MultipleCallsGenerateDifferentIds()
        {
            // Arrange
            var command1 = new CreatePersonCommand { FirstName = "John", LastName = "Doe" };
            var command2 = new CreatePersonCommand { FirstName = "Jane", LastName = "Smith" };

            // Act
            var result1 = await _handler.Handle(command1, CancellationToken.None);
            var result2 = await _handler.Handle(command2, CancellationToken.None);

            // Assert
            Assert.AreNotEqual(result1.Id, result2.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMediator_ThrowsArgumentNullException()
        {
            // Act
            new CreatePersonCommandHandler(null);
        }

        [TestMethod]
        public async Task Handle_LongNames_HandlesCorrectly()
        {
            // Arrange
            var command = new CreatePersonCommand
            {
                FirstName = "VeryLongFirstNameThatExceedsNormalExpectations",
                LastName = "VeryLongLastNameThatExceedsNormalExpectations",
                PhoneNumber = "123-456-7890"
            };

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(command.FirstName, result.FirstName);
            Assert.AreEqual(command.LastName, result.LastName);
        }

        [TestMethod]
        public async Task Handle_SpecialCharactersInNames_HandlesCorrectly()
        {
            // Arrange
            var command = new CreatePersonCommand
            {
                FirstName = "Jean-Pierre",
                LastName = "O'Connor",
                PhoneNumber = "+1 (555) 123-4567"
            };

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.AreEqual("Jean-Pierre", result.FirstName);
            Assert.AreEqual("O'Connor", result.LastName);
            Assert.AreEqual("+1 (555) 123-4567", result.PhoneNumber);
        }
    }
}