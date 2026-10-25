using StarfrontControl.Service;
using Xunit;
namespace StarfrontControl.Service.Tests;
public class SurveyFramingTests {
    [Theory][InlineData(double.NaN,0,4,3,0)][InlineData(360,0,4,3,0)][InlineData(1,91,4,3,0)][InlineData(1,0,0,3,0)][InlineData(1,0,4,30,0)]
    public void InvalidFieldIsRejected(double ra,double dec,double w,double h,double rotation){var field=new SurveyField(ra,dec,w,h,rotation);Assert.False(field.Valid);Assert.Throws<ArgumentException>(()=>field.Wcs());}
    [Fact]public void CameraCenterAspectAndPositionAngleAreEncoded(){
        var wcs=new SurveyField(324.74,57.489,4.6,3.07,0).Wcs();Assert.Equal(512,wcs["NAXIS1"]);Assert.Equal(342,wcs["NAXIS2"]);Assert.Equal(324.74,wcs["CRVAL1"]);Assert.True((double)wcs["CD1_1"]<0);Assert.Equal(0d,wcs["CD1_2"]);Assert.True((double)wcs["CD2_2"]>0);
        var rotated=new SurveyField(324.74,57.489,4.6,3.07,90).Wcs();Assert.True((double)rotated["CD1_2"]>0);Assert.True(Math.Abs((double)rotated["CD2_2"])<1e-10);Assert.True((double)rotated["CD2_1"]>0);
    }
}
