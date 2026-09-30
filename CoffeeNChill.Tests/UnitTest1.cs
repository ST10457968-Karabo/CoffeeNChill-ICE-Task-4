namespace CoffeeNChill.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void AdditionTest_ShouldReturnCorrectTotal()
        {
            // Arrange
            int firstNumber = 5;
            int secondNumber = 5;

            // Act
            int result = firstNumber + secondNumber;

            // Assert
            Assert.Equal(10, result);
        }
    }
}