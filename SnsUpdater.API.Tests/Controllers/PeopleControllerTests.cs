using System;
using System.Threading.Tasks;
using System.Web.Http.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MediatR;
using SnsUpdater.API.Controllers;
using SnsUpdater.API.Models;
using SnsUpdater.API.Commands;

namespace SnsUpdater.API.Tests.Controllers
{
    [TestClass]
    public class PeopleControllerTests
    {
        private Mock<IMediator> _mediatorMock;
        private PeopleController _controller;

        [TestInitialize]
        public void Setup()
        {
            _mediatorMock = new Mock<IMediator>();
            _controller = new PeopleController(_mediatorMock.Object);
        }

        [TestMethod]
        public async Task Post_ValidPerson_ReturnsCreatedResult()
        {
            // Arrange
            var person = new Person
            {
                FirstName = "John",
                LastName = "Doe",
                PhoneNumber = "123-456-7890"
            };

            var commandResult = new CreatePersonResult
            {
                Id = 1234,
                FirstName = "John",
                LastName = "Doe",
                PhoneNumber = "123-456-7890",
                Success = true,
                Message = "Person created successfully."
            };

            _mediatorMock.Setup(m => m.Send(It.IsAny<CreatePersonCommand>(), default))
                .ReturnsAsync(commandResult);

            // Act
            var result = await _controller.Post(person);

            // Assert
            Assert.IsNotNull(result);
            var createdResult = result as CreatedNegotiatedContentResult<CreatePersonResult>;
            Assert.IsNotNull(createdResult);
            Assert.AreEqual("api/people/1234", createdResult.Location.ToString());
            Assert.AreEqual(commandResult.Id, createdResult.Content.Id);
            Assert.AreEqual(commandResult.FirstName, createdResult.Content.FirstName);
        }

        [TestMethod]
        public async Task Post_NullPerson_ReturnsBadRequest()
        {
            // Act
            var result = await _controller.Post(null);

            // Assert
            Assert.IsNotNull(result);
            var badRequestResult = result as BadRequestErrorMessageResult;
            Assert.IsNotNull(badRequestResult);
            Assert.AreEqual("Person data is required.", badRequestResult.Message);
            
            _mediatorMock.Verify(m => m.Send(It.IsAny<CreatePersonCommand>(), default), Times.Never);
        }

        [TestMethod]
        public async Task Post_CommandReturnsFailure_ReturnsBadRequest()
        {
            // Arrange
            var person = new Person
            {
                FirstName = "John",
                LastName = "Doe",
                PhoneNumber = "123-456-7890"
            };

            var commandResult = new CreatePersonResult
            {
                Success = false,
                Message = "Validation failed"
            };

            _mediatorMock.Setup(m => m.Send(It.IsAny<CreatePersonCommand>(), default))
                .ReturnsAsync(commandResult);

            // Act
            var result = await _controller.Post(person);

            // Assert
            Assert.IsNotNull(result);
            var badRequestResult = result as BadRequestErrorMessageResult;
            Assert.IsNotNull(badRequestResult);
            Assert.AreEqual("Validation failed", badRequestResult.Message);
        }

        [TestMethod]
        public async Task Post_MediatorThrowsException_ReturnsInternalServerError()
        {
            // Arrange
            var person = new Person
            {
                FirstName = "John",
                LastName = "Doe",
                PhoneNumber = "123-456-7890"
            };

            _mediatorMock.Setup(m => m.Send(It.IsAny<CreatePersonCommand>(), default))
                .ThrowsAsync(new InvalidOperationException("Database error"));

            // Act
            var result = await _controller.Post(person);

            // Assert
            Assert.IsNotNull(result);
            var errorResult = result as InternalServerErrorResult;
            Assert.IsNotNull(errorResult);
        }

        [TestMethod]
        public async Task Post_InvalidModelState_ReturnsBadRequest()
        {
            // Arrange
            var person = new Person
            {
                FirstName = "John",
                LastName = "Doe",
                PhoneNumber = "123-456-7890"
            };

            _controller.ModelState.AddModelError("FirstName", "First name is required");

            // Act
            var result = await _controller.Post(person);

            // Assert
            Assert.IsNotNull(result);
            var badRequestResult = result as InvalidModelStateResult;
            Assert.IsNotNull(badRequestResult);
            
            _mediatorMock.Verify(m => m.Send(It.IsAny<CreatePersonCommand>(), default), Times.Never);
        }

        [TestMethod]
        public async Task Post_MapsPersonToCommandCorrectly()
        {
            // Arrange
            var person = new Person
            {
                FirstName = "Jane",
                LastName = "Smith",
                PhoneNumber = "555-123-4567"
            };

            CreatePersonCommand capturedCommand = null;
            _mediatorMock.Setup(m => m.Send(It.IsAny<CreatePersonCommand>(), default))
                .Callback<IRequest<CreatePersonResult>, System.Threading.CancellationToken>((cmd, ct) => 
                    capturedCommand = cmd as CreatePersonCommand)
                .ReturnsAsync(new CreatePersonResult { Success = true, Id = 1 });

            // Act
            await _controller.Post(person);

            // Assert
            Assert.IsNotNull(capturedCommand);
            Assert.AreEqual("Jane", capturedCommand.FirstName);
            Assert.AreEqual("Smith", capturedCommand.LastName);
            Assert.AreEqual("555-123-4567", capturedCommand.PhoneNumber);
        }

        [TestMethod]
        public void Get_ReturnsExpectedValues()
        {
            // Act
            var result = _controller.Get();

            // Assert
            Assert.IsNotNull(result);
            var values = result as string[];
            Assert.IsNotNull(values);
            Assert.AreEqual(2, values.Length);
            Assert.AreEqual("value1", values[0]);
            Assert.AreEqual("value2", values[1]);
        }

        [TestMethod]
        public void GetById_ReturnsExpectedValue()
        {
            // Act
            var result = _controller.Get(5);

            // Assert
            Assert.AreEqual("value", result);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMediator_ThrowsArgumentNullException()
        {
            // Act
            new PeopleController(null);
        }
    }
}