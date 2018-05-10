using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using ParkingNexus.Models;
using System.Data.Entity.Core.Objects;
using System.Data.Entity;
using System.Data;
using System.Configuration;
using ParkingNexus.BAL;
using ParkingNexus.BAL.Model;
using ParkingNexus.BAL.ViewModel;
using System.Collections.Specialized;
using Braintree;
using ParkingNexus.Helpers;
using System.Net;

namespace ParkingNexus.Controllers
{
    public class ParkingController : Controller
    {

        #region variables of Braintree api
        public string transactionId = "";
        public IBraintreeConfiguration config = new BraintreeConfiguration();
        public static readonly TransactionStatus[] transactionSuccessStatuses = {
                                                                                    TransactionStatus.AUTHORIZED,
                                                                                    TransactionStatus.AUTHORIZING,
                                                                                    TransactionStatus.SETTLED,
                                                                                    TransactionStatus.SETTLING,
                                                                                    TransactionStatus.SETTLEMENT_CONFIRMED,
                                                                                    TransactionStatus.SETTLEMENT_PENDING,
                                                                                    TransactionStatus.SUBMITTED_FOR_SETTLEMENT
                                                                                };
        #endregion

        PaymentServiceManager objServicemanager = new PaymentServiceManager();
        AccountServiceManager objAccountManager = new AccountServiceManager();
        clsPayment objPayment = new Models.clsPayment();
        BAL.clsParkingRequest objParkingRequest = new BAL.clsParkingRequest();
        string LivePath = ConfigurationManager.AppSettings["LiveURL"];
        BAL.clsFeedback objClsFeedback = new BAL.clsFeedback();
        ParkingNexusBLL.Utility objUtility = new ParkingNexusBLL.Utility();
        ParkingnexusEntities objContext = new ParkingnexusEntities();
        string[] days = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        public static int ParkId;

        public ActionResult Index()
        {
            return View();
        }

        #region ParkingDetailsByID
        public ActionResult getparkingDetailsByID(int parkinginfo)
        {
            List<PT_Feedback> objFeedback = new List<PT_Feedback>();
            try
            {
                if (parkinginfo > 0)
                {
                    objFeedback = (from review in objContext.PT_Feedback where review.ParkingId == parkinginfo select review).ToList();
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.Write(e.Message);
            }
            return PartialView("_ParkingDetails", objFeedback);
        }
        #endregion

        #region PayNow&Booking

        [HttpPost]
        public bool BookParking(int parkingid, string fromTime, string toTime, string fromDate, string toDate, string totalHous, string price, string parkingtax, string Unitprice, string TransFee, string loginopt = "", decimal TotalSpecialCharges = 0.0M, int monthly = 0, int endday = 0, int isMultipleInOut = 0, string freeHoursUpTo = "", int multipleInOutOption = 0)
        {
            bool result = false;
            parkinginfo objParkinginfo = new parkinginfo();
            string userid = Convert.ToString(Session["userID"]);
            objParkinginfo.fromdate = fromDate;
            objParkinginfo.todate = toDate;
            objParkinginfo.fromTime = fromTime;
            objParkinginfo.toTime = toTime;
            objParkinginfo.parkingTax = parkingtax;
            objParkinginfo.Unitrate = Unitprice;
            objParkinginfo.transactionFee = TransFee;
            objParkinginfo.TotalSpecialCharges = TotalSpecialCharges;
            objParkinginfo.monthly = monthly;
            objParkinginfo.endday = endday;
            objParkinginfo.parkingID = Convert.ToInt32(parkingid);
            objParkinginfo.Amount = price;
            objParkinginfo.TotalAmount = price;
            objParkinginfo.hasFreeHours = !string.IsNullOrWhiteSpace(freeHoursUpTo);
            objParkinginfo.multipleInOutOption = multipleInOutOption;
            if (objParkinginfo.hasFreeHours)
            {
                objParkinginfo.freeHoursUpTo = Convert.ToDateTime(freeHoursUpTo);
                double totalFrHr = (objParkinginfo.freeHoursUpTo - Convert.ToDateTime(toDate + " " + toTime)).TotalHours;
                double totalFrMin = Math.Ceiling((totalFrHr - Math.Floor(totalFrHr)) * 60);
                objParkinginfo.TotalFreeHours = Math.Floor(totalFrHr) + " hr" + (totalFrMin > 0 ? " " + totalFrMin + " mins" : "");
            }
            else
            {
                objParkinginfo.TotalFreeHours = "";
            }

            var parkinginfo = (from parking in objContext.PT_Parking
                               where parking.ParkingId == parkingid
                               select parking).FirstOrDefault();
            if (parkinginfo != null)
            {
                objParkinginfo.parkingname = parkinginfo.PlaceName;
                objParkinginfo.Address = parkinginfo.Address;
                objParkinginfo.multipleInOutRate = Convert.ToDecimal(parkinginfo.MultipleInOutRate);
                objParkinginfo.isParkingMultipleInOut = parkinginfo.AmenityIDS.Split(',').Contains("8");
                objParkinginfo.isMultipleInOut = Convert.ToBoolean(isMultipleInOut);
                objParkinginfo.totalHours = totalHous;
                Session["Latitude"] = parkinginfo.Latitude + "_" + parkinginfo.Longitude;
                Session["BookingDetails"] = objParkinginfo;
                if (parkingid > 0 && userid != "" || (loginopt != "" && loginopt == "Guest"))
                {
                    return true;
                }
            }
            return result;
        }

        [ValidateInput(false)]
        public ActionResult paynow(int page = 0, string ResponseText = "", int isMultipleInOut = 0, int multipleInOutOption = 0)
        {
            parkinginfo objParkinginfo = new parkinginfo();
            List<PK_ParkingMultipleInOutRate> multipleInOutRates = new List<PK_ParkingMultipleInOutRate>();
            ParkingNexusBLL.clsSearchLocation objclsSearchLocation = new ParkingNexusBLL.clsSearchLocation();
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var gateway = config.GetGateway();
                var clientToken = gateway.ClientToken.generate();
                ViewBag.ClientToken = clientToken;
                ViewBag.monthlyOneTime = 0;
                if (page > 0)
                {
                    ViewBag.openpopup = true;
                }
                if (Session["BookingDetails"] != null)
                {
                    objParkinginfo = (parkinginfo)Session["BookingDetails"];
                    if (objParkinginfo.isParkingMultipleInOut && isMultipleInOut == 1)
                    {
                        objParkinginfo.isMultipleInOut = true;
                    }
                    else
                    {
                        objParkinginfo.isMultipleInOut = false;
                    }
                    if (objParkinginfo.isParkingMultipleInOut)
                    {
                        multipleInOutRates = (from parking in objContext.PK_ParkingMultipleInOutRate
                                              where parking.parkingId == objParkinginfo.parkingID
                                              select parking).OrderBy(x => x.Hours).OrderBy(x => x.Hours).ToList();
                        ViewBag.multipleInOutRates = multipleInOutRates;
                        objParkinginfo.isParkingMultipleInOut =( multipleInOutRates!=null && multipleInOutRates.Count>0);
                    }
                    decimal TotalAmount = Convert.ToDecimal(objParkinginfo.Amount);
                    decimal TransactionFee = Convert.ToDecimal(objParkinginfo.transactionFee);
                    decimal ParkingTax = Convert.ToDecimal(objParkinginfo.parkingTax);
                    decimal Amount = 0m;
                    decimal ConvenienceTax = 0m;
                    decimal AmountWithoutConvenienceFee = 0m;
                    PK_ParkingMultipleInOutRate multipleInOutRate = multipleInOutRates != null ? multipleInOutRates.Where(x => x.Id == multipleInOutOption).FirstOrDefault() : null;
                    objParkinginfo.multipleInOutRate = multipleInOutRate != null ? multipleInOutRate.Rate :( multipleInOutRates!=null && multipleInOutRates.Count>0? multipleInOutRates.FirstOrDefault().Rate:0);
                    objParkinginfo.multipleInOutOption = multipleInOutOption <= 0 ? (multipleInOutRates != null && multipleInOutRates.Count > 0 ? multipleInOutRates.FirstOrDefault().Id : 0) : multipleInOutOption;
                    TotalAmount = (TotalAmount - TransactionFee) + (objParkinginfo.isMultipleInOut ? objParkinginfo.multipleInOutRate : 0);
                    AmountWithoutConvenienceFee = (Convert.ToDecimal(TotalAmount) - ConvenienceTax);
                    Amount = AmountWithoutConvenienceFee * Convert.ToDecimal(0.8);
                    ParkingTax = AmountWithoutConvenienceFee - Amount;
                    TotalAmount = (TotalAmount + TransactionFee);
                    objParkinginfo.TotalAmount = TotalAmount.ToString();
                    objParkinginfo.parkingTax = ParkingTax.ToString();
                    objParkinginfo.transactionFee = TransactionFee.ToString();
                    Session["BookingDetails"] = objParkinginfo;
                    if(objParkinginfo.monthly == 1)
                    {
                        double totalMonth = DateTimeHelper.MonthDifference(Convert.ToDateTime(objParkinginfo.fromdate), Convert.ToDateTime(objParkinginfo.todate));
                        objParkinginfo.priceDetails = new List<priceInfo>();
                        if (objParkinginfo.endday == 1 && totalMonth < 2)
                        {
                            ViewBag.monthlyOneTime = 1;
                        }
                        if (totalMonth < 2)
                        {
                            objParkinginfo.priceDetails.Add(new priceInfo { StartDate = objParkinginfo.fromdate, EndDate = objParkinginfo.todate, Price = objParkinginfo.TotalAmount });
                        }
                        else
                        {
                            DateTime StartDate = Convert.ToDateTime(objParkinginfo.fromdate);
                            DateTime endDate = Convert.ToDateTime(objParkinginfo.todate).AddDays(1);
                            while (StartDate <= endDate)
                            {
                                DateTime nextMonthDate = StartDate.AddMonths(1).AddDays(-1);
                                TransactionFee = objclsSearchLocation.GetTransactionFee(StartDate.ToString(), nextMonthDate.ToString());
                                Dictionary<string, object> dt = objclsSearchLocation.getParkingMonthlyCharge(StartDate, nextMonthDate > endDate ? endDate : nextMonthDate, objParkinginfo.parkingID, getLoginUserEmail(), nextMonthDate > endDate);
                                TotalAmount = Math.Round(Convert.ToDecimal(dt["TotalCharge"]), 2) + (objParkinginfo.isMultipleInOut ? objParkinginfo.multipleInOutRate : 0m);
                                Amount = 0m;
                                ParkingTax = 0m;
                                ConvenienceTax = 0m;
                                AmountWithoutConvenienceFee = 0m;
                                AmountWithoutConvenienceFee = (Convert.ToDecimal(TotalAmount) - ConvenienceTax);
                                Amount = AmountWithoutConvenienceFee * Convert.ToDecimal(0.8);
                                ParkingTax = AmountWithoutConvenienceFee - Amount;
                                TotalAmount = (TotalAmount + TransactionFee);
                                if (nextMonthDate <= endDate)
                                {
                                    objParkinginfo.priceDetails.Add(new priceInfo { StartDate = StartDate.ToShortDateString(), EndDate = nextMonthDate.ToShortDateString(), Price = TotalAmount.ToString() });
                                    StartDate = StartDate.AddMonths(1);
                                }
                                else
                                {
                                    var totalExtraDays = (endDate - StartDate).TotalDays;
                                    objParkinginfo.priceDetails.Add(new priceInfo { StartDate = StartDate.ToShortDateString(), EndDate = endDate.AddDays(-1).ToShortDateString(), Price = TotalAmount.ToString() });
                                    StartDate = nextMonthDate;
                                }
                            }
                        }
                    }
                    int userid = 0;
                    if (Session["userID"] != null && Convert.ToInt32(Session["userID"]) > 0)
                    {
                        userid = Convert.ToInt32(Session["userID"]);
                    }
                    else
                    {
                        userid = Convert.ToInt32(Session["CheckoutID"]);
                    }
                    if (userid > 0)
                    {
                        List<VehicleViewModel.AddVehicle> MycarDetails = new VehicleServiceManager().AllVehicle(userid);
                        objParkinginfo.MyVeichle = MycarDetails;
                        List<PaymentViewModel.AddPaymentMethod> MyallCard = new PaymentServiceManager().AllPaymentDetails(userid);
                        objParkinginfo.Mycard = MyallCard;
                    }
                    else
                    {
                        return RedirectToAction("Login", "Account");
                    }
                    ViewBag.totalprice = objParkinginfo.Amount;
                    ViewBag.paymentError = ResponseText;
                    return View(objParkinginfo);
                }
                else
                {
                    return RedirectToAction("Login", "Account");
                }

            }
            catch (Exception ex)
            {
                HandleError.ErrorMsg = ex.Message;
                return RedirectToAction("Index", "Error");
            }
        }

        [HttpPost]
        public ActionResult paynow(string payment_method_nonce, int isMultipleInOut, int isFreeHoursAccepted, bool isMonthly = false)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            parkinginfo objParkinginfo = new parkinginfo();
            if (Session["BookingDetails"] != null)
            {

                TempData["hdfselectcarID"] = Convert.ToInt32(Request["hdfselectcarID"] != "" ? Request["hdfselectcarID"] : "0");
                objParkinginfo = (parkinginfo)Session["BookingDetails"];
                try
                {
                    TimeSpan tz = DateTimeHelper.zone.GetUtcOffset(DateTime.UtcNow);
                    ParkingNexusBLL.clsSearchLocation objclsSearchLocation = new ParkingNexusBLL.clsSearchLocation();
                    if (ModelState.IsValid)
                    {
                        PT_Users userdetail = null;
                        if(Convert.ToString(Session["userID"]) != null && Convert.ToString(Session["userID"]) != "")
                        {
                            objParkingRequest.UserId = Convert.ToInt32(Session["userID"].ToString());
                            userdetail = objContext.PT_Users.Where(x => x.UserId == objParkingRequest.UserId).FirstOrDefault();
                        }
                        else if(Convert.ToString(Session["CheckoutID"]) != null && Convert.ToString(Session["CheckoutID"]) != "")
                        {
                            objParkingRequest.UserId = Convert.ToInt32(Session["CheckoutID"].ToString());
                            userdetail = objContext.PT_Users.Where(x => x.UserId == objParkingRequest.UserId).FirstOrDefault();
                        }
                        else
                        {
                            return RedirectToAction("Login", "Account");
                        }
                        var gateway = config.GetGateway();
                        decimal amount = 0.0m;

                        amount = Convert.ToDecimal(objParkinginfo.TotalAmount);
                        var nonce = payment_method_nonce;
                        var request = new TransactionRequest
                        {
                            Amount = amount,
                            PaymentMethodNonce = payment_method_nonce,
                            Options = new TransactionOptionsRequest
                            {
                                SubmitForSettlement = true
                            }
                        };
                        #region transaction
                        Result<Transaction> result = gateway.Transaction.Sale(request);
                        if (result.IsSuccess())
                        {
                            Transaction transaction = result.Target;
                            transactionId = transaction.Id;
                            Session["BookingDetails"] = null;
                            objParkingRequest.ParkingId = objParkinginfo.parkingID;
                            objParkingRequest.CardId = Convert.ToInt32(objParkinginfo.CardID);
                            objParkingRequest.parkingTax = Convert.ToDecimal(objParkinginfo.parkingTax);
                            objParkingRequest.UnitPrice = Convert.ToDecimal(objParkinginfo.Unitrate);
                            objParkingRequest.ParkingtransactionFee = Convert.ToDecimal(objParkinginfo.transactionFee);
                            objParkingRequest.CarId = Convert.ToInt32(Request["hdfselectcarID"] != "" ? Request["hdfselectcarID"] : "0");
                            objParkingRequest.Charges = Convert.ToDecimal(objParkinginfo.Amount);
                            objParkingRequest.RequestedFromDate = objParkinginfo.fromdate;
                            objParkingRequest.RequestedFromTime = Convert.ToDateTime(objParkinginfo.fromTime).ToString("HH:mm");
                            objParkingRequest.RequestedToDate = objParkinginfo.todate;
                            objParkingRequest.RequestedToTime = Convert.ToDateTime(objParkinginfo.toTime).ToString("HH:mm");
                            objParkingRequest.Status = "Paid";
                            objParkingRequest.transactionNumber = transactionId.ToString();
                            objParkingRequest.Response = "Success";
                            objParkingRequest.RequestDateTimeOffSet = tz.ToString();
                            objParkingRequest.isMultipleInOut = objParkinginfo.isMultipleInOut;
                            objParkingRequest.multipleInOutAmount = objParkinginfo.multipleInOutRate;
                            objParkingRequest.isFreeHr = isFreeHoursAccepted;
                            var multipleInOutRates = (from parking in objContext.PK_ParkingMultipleInOutRate
                                                      where parking.parkingId == objParkinginfo.parkingID && parking.Id == objParkinginfo.multipleInOutOption
                                                      select parking).OrderBy(x => x.Hours).FirstOrDefault();
                            if (multipleInOutRates != null)
                            {
                                objParkingRequest.multipleInOutHours = multipleInOutRates.Hours;
                            }
                            else
                            {
                                objParkingRequest.multipleInOutHours = 0;
                            }
                            objParkingRequest.isMonthly = isMonthly;
                            if (isFreeHoursAccepted == 1 && (objParkinginfo.freeHoursUpTo != null || objParkinginfo.freeHoursUpTo != DateTime.MinValue))
                            {
                                objParkingRequest.FreeToDate = objParkinginfo.freeHoursUpTo.ToShortDateString();
                                objParkingRequest.FreeToTime = objParkinginfo.freeHoursUpTo.ToShortTimeString();
                            }
                            int ParkReqRetID = objParkingRequest.AddUpdateParkingRequest();
                            objParkinginfo.TransactionNumer = transactionId.ToString();
                            ParkingNexusBLL.clsPayment pay = new ParkingNexusBLL.clsPayment();
                            DataSet ds = pay.addRequestPayment(ParkReqRetID, "Parking", amount, transaction.Id, "Credit Card", "Paid", Convert.ToDateTime(DateTime.Now), "", "", "");
                            //send email code start here
                            try
                            {
                                objUtility.SendParkingEmailUpdate(ParkReqRetID, "confirmed");
                                if (Convert.ToString(Session["CheckoutID"]) != null && Convert.ToString(Session["CheckoutID"]) != "")
                                {
                                    string Livepath = ConfigurationManager.AppSettings["LiveSitepath"];
                                    string VerifyLink = "<p>Please, Click <a href=" + Livepath + "Account/VerifyAccount/" + userdetail.UserId + ">here</a> to Verfiy your Account.</p>";
                                    AccountServiceManager AccountService = new AccountServiceManager();
                                    ListDictionary Tlst = new ListDictionary();
                                    string ToEmailId = userdetail.Email;
                                    string FromEmailId = "Parking-Nexus<" + ConfigurationManager.AppSettings["BookParking"].ToString() + ">";                                 
                                    string temp = HttpContext.Server.MapPath("~/Content/Template/Email/SignUpVerifyMail.html");
                                    string SubjectLine = "Parking Nexus Verification";
                                    string template = temp;
                                    Tlst.Add("@@Link", VerifyLink);
                                    AccountService.SendHTMLMail(FromEmailId, ToEmailId, SubjectLine, template, Tlst, "");
                                }
                            }
                            catch (Exception e)
                            {
                                ViewBag.test = e.InnerException.Message;
                                HandleError.ErrorMsg = e.InnerException.Message;
                            }
                            if (Convert.ToString(Session["CheckoutID"]) != null && Convert.ToString(Session["CheckoutID"]) != "")
                            {
                                return RedirectToAction("OrderDetails", new { orderdetails = objParkingRequest.Response + "$" + result, reqid = ParkReqRetID });
                            }
                            else
                            {
                                return RedirectToAction("OrderDetails", new { orderdetails = objParkingRequest.Response + "$" + result, reqid = ParkReqRetID });
                            }
                        }
                        else if (result.Transaction != null)
                        {
                            transactionId = result.Transaction.Id;
                            if (result.Transaction.ProcessorResponseCode == "2038")
                            {
                                HandleError.ErrorMsg = "Processor Declined.";
                                return RedirectToAction("paynow", "Parking", new { ResponseText = "Processor Declined" });
                            }
                            else if (result.Transaction.ProcessorResponseCode == "2001")
                            {
                                HandleError.ErrorMsg = "Insufficient Funds";
                                return RedirectToAction("paynow", "Parking", new { ResponseText = "Insufficient Funds" });
                            }
                            else if (result.Transaction.ProcessorResponseCode == "2005")
                            {
                                HandleError.ErrorMsg = "Invalid Credit Card Number";
                                return RedirectToAction("paynow", "Parking", new { ResponseText = "Invalid Credit Card Number" });
                            }
                            else
                            {
                                HandleError.ErrorMsg = "some error occured";
                                return RedirectToAction("Index", "Error");
                            }
                            //Show(transactionId); 
                        }
                        else
                        {
                            string errorMessages = "";
                            foreach (ValidationError error in result.Errors.DeepAll())
                            {
                                errorMessages += "Error: " + (int)error.Code + " - " + error.Message + "\n";
                            }
                            // Response.Redirect("PaymentFailed.aspx");
                            ViewBag.PaymentFail = "Payment Failed";

                            int userid = Convert.ToInt32(Session["userID"]);
                            if (userid <= 0)
                            {
                                userid = Convert.ToInt32(Session["CheckoutID"]);
                            }
                            List<VehicleViewModel.AddVehicle> MycarDetails = new VehicleServiceManager().AllVehicle(userid);
                            objParkinginfo.MyVeichle = MycarDetails;
                            List<PaymentViewModel.AddPaymentMethod> MyallCard = new PaymentServiceManager().AllPaymentDetails(userid);
                            objParkinginfo.Mycard = MyallCard;
                            ViewBag.totalprice = objParkinginfo.TotalAmount;
                            ModelState.Clear();
                            return RedirectToAction("paynow", "Parking", new { ResponseText = "Payment failed please check card details" });
                        }
                        #endregion
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.Write(ex.Message);
                }
                return View(objParkinginfo);
            }
            else
            {
                return RedirectToAction("Login", "Account");
            }
        }
       
        [HttpPost]
        public ActionResult paynowmonthly(parkinginfo objparkingInfo, string payment_method_nonce, int isMultipleInOut, bool isMonthly = true)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            PT_Users userdetail = null;
            TempData["hdfselectcarID"] = Convert.ToInt32(Request["hdfselectcarID"] != "" ? Request["hdfselectcarID"] : "0");
            #region check user is login or Guest CheckOut   
            if (Convert.ToString(Session["userID"]) != null && Convert.ToString(Session["userID"]) != "")
            {
                objParkingRequest.UserId = Convert.ToInt32(Session["userID"].ToString());
                userdetail = objContext.PT_Users.Where(x => x.UserId == objParkingRequest.UserId).FirstOrDefault();
            }
            else if (Convert.ToString(Session["CheckoutID"]) != null && Convert.ToString(Session["CheckoutID"]) != "")
            {
                objParkingRequest.UserId = Convert.ToInt32(Session["CheckoutID"].ToString());
                userdetail = objContext.PT_Users.Where(x => x.UserId == objParkingRequest.UserId).FirstOrDefault();
            }
            else
            {
                return RedirectToAction("Login", "Account");
            }
            #endregion
            TimeSpan tz = DateTimeHelper.zone.GetUtcOffset(DateTime.UtcNow);
            if (Session["BookingDetails"] != null)
            {
                objparkingInfo = (parkinginfo)Session["BookingDetails"];
                #region transaction
                double totalMonths = DateTimeHelper.MonthDifference(Convert.ToDateTime(objparkingInfo.fromdate), Convert.ToDateTime(objparkingInfo.todate));
                if (totalMonths < 2 && objparkingInfo.endday == 1)
                {
                    return paynow(payment_method_nonce, isMultipleInOut, 0, true);
                }
                else
                {
                    var gateway = config.GetGateway();
                    Customer customer = null;
                    var customerFindRequest = new CustomerSearchRequest().Email.Is(userdetail.Email);
                    ResourceCollection<Customer> collection = gateway.Customer.Search(customerFindRequest);
                    if (collection != null && collection.MaximumCount > 0)
                    {
                        customer = collection.FirstItem;
                    }
                    if (customer == null)
                    {
                        var _customerRequest = new CustomerRequest
                        {
                            CustomerId = "customer_" + userdetail.UserId.ToString(),
                            FirstName = userdetail.FirstName,
                            LastName = userdetail.LastName,
                            Email = userdetail.Email
                        };
                        Result<Customer> _customerResult = gateway.Customer.Create(_customerRequest);
                        if (_customerResult.IsSuccess())
                        {
                            customer = _customerResult.Target;
                        }
                    }
                    if (customer != null)
                    {
                        string cardToken;
                        var payMethod = new PaymentMethodRequest
                        {
                            CustomerId = customer.Id,
                            PaymentMethodNonce = payment_method_nonce
                        };
                        Result<PaymentMethod> payMethodResult = gateway.PaymentMethod.Create(payMethod);
                        if (payMethodResult.IsSuccess())
                        {
                            cardToken = payMethodResult.Target.Token;
                            DateTime billingDate = Convert.ToDateTime(objparkingInfo.fromdate + " " + objparkingInfo.fromTime);
                            DateTime LastBillingDate = Convert.ToDateTime(objparkingInfo.todate + " " + objparkingInfo.toTime);
                            if (billingDate < DateTime.Now)
                            {
                                billingDate = DateTime.Now.AddMinutes(1);
                            }
                            var _SubscriptionRequest = new SubscriptionRequest
                            {
                                PaymentMethodToken = cardToken,
                                PlanId = "Monthly_Pass",
                                FirstBillingDate = billingDate,
                                Price = Convert.ToDecimal(objparkingInfo.TotalAmount)
                            };
                            if (objparkingInfo.endday == 1)
                            {
                                _SubscriptionRequest = new SubscriptionRequest
                                {
                                    PaymentMethodToken = cardToken,
                                    PlanId = "Monthly_Pass",
                                    FirstBillingDate = billingDate,
                                    Price = Convert.ToDecimal(objparkingInfo.TotalAmount),
                                    NumberOfBillingCycles = (int)Math.Ceiling(totalMonths),

                                };
                            }
                            Result<Subscription> result = gateway.Subscription.Create(_SubscriptionRequest);
                            if (result.IsSuccess())
                            {
                                Subscription s = result.Target;
                                transactionId = s.Id;
                                //OnSuccessfulPaymentBraintree(subscription);
                                ParkingNexusBLL.clsParkingMonthlySubscription objSubscription = new ParkingNexusBLL.clsParkingMonthlySubscription();
                                objSubscription.SubscriptionID = s.Id;
                                objSubscription.BillingPeriodEndDate = s.BillingPeriodEndDate;
                                objSubscription.BillingPeriodStartDate = s.BillingPeriodStartDate;
                                objSubscription.CreatedAt = s.CreatedAt;
                                objSubscription.CurrentBillingCycle = Convert.ToInt32(s.CurrentBillingCycle);
                                objSubscription.DaysPastDue = Convert.ToInt32(s.DaysPastDue);
                                objSubscription.FailureCount = Convert.ToInt32(s.FailureCount);
                                objSubscription.FirstBillingDate = s.FirstBillingDate;
                                objSubscription.NextBillAmount = Convert.ToDecimal(s.NextBillAmount);
                                objSubscription.NextBillingDate = s.NextBillingDate;
                                objSubscription.parkingID = objparkingInfo.parkingID;
                                objSubscription.PlanId = s.PlanId;
                                objSubscription.Price = Convert.ToDecimal(objparkingInfo.Amount);
                                objSubscription.ParkingTax = Convert.ToDecimal(objparkingInfo.parkingTax);
                                objSubscription.TransactionFee = Convert.ToDecimal(objparkingInfo.transactionFee);
                                objSubscription.Status = s.Status.ToString();
                                objSubscription.UpdatedAt = s.UpdatedAt;
                                objSubscription.userID = userdetail.UserId;
                                objSubscription.carID = Convert.ToInt32(Request["hdfselectcarID"] != "" ? Request["hdfselectcarID"] : "0");
                                objSubscription.isMultipleInOut = objparkingInfo.isMultipleInOut;
                                objSubscription.multipleInOutPrice = objparkingInfo.multipleInOutRate;
                                if (objparkingInfo.endday == 1)
                                {
                                    objSubscription.LastBillingDate = LastBillingDate;
                                }
                                objSubscription.ID = objSubscription.ADD_ParkingMonthlySubscription();
                                if (objSubscription.ID > 0)
                                {
                                    objUtility.sendSubscriptionMail(objSubscription);
                                }
                                return RedirectToAction("subscriptionDetails", new { orderdetails = "success", reqid = objSubscription.SubscriptionID, monthly = 1, isMultipleInOut = isMultipleInOut });
                            }
                            else if (result.Transaction != null)
                            {
                                if (result.Transaction.ProcessorResponseCode == "2038")
                                {
                                    HandleError.ErrorMsg = "Processor Declined.";
                                    return RedirectToAction("paynow", "Parking", new { ResponseText = "Processor Declined", isMultipleInOut = isMultipleInOut });
                                }
                                else if (result.Transaction.ProcessorResponseCode == "2001")
                                {
                                    HandleError.ErrorMsg = "Insufficient Funds";
                                    return RedirectToAction("paynow", "Parking", new { ResponseText = "Insufficient Funds", isMultipleInOut = isMultipleInOut });
                                }
                                else if (result.Transaction.ProcessorResponseCode == "2005")
                                {
                                    HandleError.ErrorMsg = "Invalid Credit Card Number";
                                    return RedirectToAction("paynow", "Parking", new { ResponseText = "Invalid Credit Card Number", isMultipleInOut = isMultipleInOut });
                                }
                                else
                                {
                                    HandleError.ErrorMsg = "some error occured";
                                    return RedirectToAction("Index", "Error");
                                }
                            }
                            else
                            {
                                string errorMessages = "";
                                foreach (ValidationError error in result.Errors.DeepAll())
                                {
                                    errorMessages += "Error: " + (int)error.Code + " - " + error.Message + "\n";
                                }
                                ViewBag.PaymentFail = "Payment Failed";
                                return RedirectToAction("paynow", "parking", new { page = 0, ResponseText = errorMessages, isMultipleInOut = isMultipleInOut });
                            }
                        }
                        else
                        {
                            ViewBag.PaymentFail = "unable to create customer payment method.";
                            return RedirectToAction("paynow", "parking", new { page = 0, ResponseText = "unable to create customer payment method." });
                        }
                    }
                    else
                    {
                        ViewBag.PaymentFail = "unable to create customer.";
                        return RedirectToAction("paynow", "parking", new { page = 0, ResponseText = "unable to create customer." });
                    }
                }
                #endregion
            }
            else
            {
                return RedirectToAction("index", "search");
            }
        }
        public string CarDetails(int page = 0)
        {
            string modalhtml = "";
            try
            {
                int pagesize = 5;
                parkinginfo objparkingInfo = new parkinginfo();
                int userid = Convert.ToInt32(Session["userID"]);
                if (userid > 0)
                {

                }
                else
                {
                    userid = Convert.ToInt32(Session["CheckoutID"]);
                }
                if (userid > 0)
                {
                    List<VehicleViewModel.AddVehicle> MycarDetails = new VehicleServiceManager().AllVehicle(userid);
                    ViewBag.totalrecord = MycarDetails.Count;
                    objparkingInfo.MyVeichle = MycarDetails.OrderByDescending(x => x.CarId).Skip(5 * page).Take(5).ToList();
                }
                ViewBag.pageNumber = page;
                modalhtml = RenderRazorViewToString("_CarDetails", objparkingInfo.MyVeichle);
               
            }
            catch (Exception ex)
            {
                HandleError.ErrorMsg = ex.Message;
                //return RedirectToAction("Index", "Error");
            }
            return modalhtml;
        }

        public string CardDetails(int page = 0)
        {
            string modalhtml = "";
            try
            {
                parkinginfo objparkingInfo = new parkinginfo();
                int userid = Convert.ToInt32(Session["userID"]);
                if (userid > 0)
                {

                }
                else
                {
                    userid = Convert.ToInt32(Session["CheckoutID"]);
                }
                if (userid > 0)
                {
                    List<PaymentViewModel.AddPaymentMethod> MyallCard = new PaymentServiceManager().AllPaymentDetails(userid);
                    ViewBag.totalrecord = MyallCard.Count;
                    objparkingInfo.Mycard = MyallCard.OrderByDescending(x => x.CardId).Skip(5 * page).Take(5).ToList();
                }
                ViewBag.pageNumber = page;
                modalhtml = RenderRazorViewToString("_CardDetails", objparkingInfo.Mycard);
            }
            catch (Exception ex)
            {
                HandleError.ErrorMsg = ex.Message;
                //return RedirectToAction("Index", "Error");
            }
            return modalhtml;
        }
        #endregion

        #region OrderDetails
        public ActionResult subscriptionDetails(string reqid = "", string orderDetails = "", int monthly = 0)
        {
            ParkingNexusBLL.clsParkingMonthlySubscription s = new ParkingNexusBLL.clsParkingMonthlySubscription();
            DataTable dt = null;
            if (!string.IsNullOrWhiteSpace(reqid))
            {
                s.SubscriptionID = reqid;
                dt = s.Get_SubscriptionDetails();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(Convert.ToString(Session["userID"])))
                {
                    s.userID = Convert.ToInt32(Session["userID"]);
                    dt = s.Get_SubscriptionList();
                }
                else
                {
                    return RedirectToAction("Login", "Account");
                }

            }
            return View(dt);
        }
        public ActionResult OrderDetails(string orderdetails = "", string reqid = "")
        {
            ViewBag.ratingUserId = Session["userid"]!=null ? Session["userid"].ToString():null;
            string[] SuccessMsg = orderdetails.Split('$');
            string[] errorMsg = orderdetails.Split('/');
            // string error = "Error";
            string status = "";
            string ToEmail = "";
            string FromEmail = ConfigurationManager.AppSettings["FromAddress"].ToString();
            string CC = ConfigurationManager.AppSettings["CCEmail"].ToString();
            string Subject = "";
            string body = "";
            List<PT_ParkingRequests> RequestVByUser = new List<PT_ParkingRequests>();
            if (orderdetails.Contains("Success"))
            {
                ViewBag.error = "Booking order has been done.";
                status = "Success";
            }
            else if (orderdetails.Contains("Failed"))
            {
                ViewBag.error = "Transaction has been denied / " + errorMsg[1];
                status = "Failed";
            }
            else if (orderdetails.Contains("CancelRefund"))
            {
                ViewBag.error = "Booking order has been cancelled & Refunded successfully";
                status = "CancelRefundSuccess";
            }
            else if (orderdetails.Contains("ErrorCanRef"))
            {
                ViewBag.error = "Cancellation Request Could not be processed " + "Error Code (" + errorMsg[0].ToString() + ")";
                status = "ErrorCanRef";
            }
            else if (orderdetails.Contains("ErrorCancelRefundAlready"))
            {
                ViewBag.error = "Request has been cancelled & Refunded already.";
                status = "ErrorCancelRefundAlready";
            }
            else if (orderdetails.Contains("ErrorCancelAlready"))
            {
                ViewBag.error = "Request has been cancelled already.";
                status = "ErrorCancelAlready";
            }
            else if (orderdetails.Contains("Cancel"))
            {
                ViewBag.error = "Booking order has been cancelled successfully";
                status = "CancelSuccess";
            }
            else
            {
                ViewBag.error = "My order";
                status = "My order";
            }
            int userid = Convert.ToInt32(Session["userid"]);
            if (userid > 0)
            {

            }
            else
            {
                userid = Convert.ToInt32(Session["CheckoutID"]);
            }
            //RequestVByUser = objContext.PT_ParkingRequests.Where(m => m.UserId == userid).ToList();
            string json = "";
            string result = "";
            Message objMessage = new Message();
            List<Message> ListMessage = new List<Message>();
            clsParkingRequest objParkingRequest = new clsParkingRequest();
            List<clsParkingRequest> lstParkingRequest = new List<clsParkingRequest>();
            DataTable Obdt = new DataTable();
            try
            {
                objParkingRequest.Id = Convert.ToInt32(reqid);
                Obdt = objParkingRequest.GetParkingRequestByRequestId();
                if (Obdt.Rows.Count > 0)
                {
                    for (Int32 i = 0; i < Obdt.Rows.Count; i++)
                    {
                        objParkingRequest = new clsParkingRequest();
                        objParkingRequest.Id = Convert.ToInt32(Obdt.Rows[i]["Id"]);
                        objParkingRequest.ParkingId = Convert.ToInt32(Obdt.Rows[i]["ParkingId"]);
                        objParkingRequest.UserId = Convert.ToInt32(Obdt.Rows[i]["UserId"]);
                        objParkingRequest.CarId = Convert.ToInt32(Obdt.Rows[i]["CarId"]);
                        objParkingRequest.RequestedFromDate = Convert.ToString(Obdt.Rows[i]["RequestedFromDate"]);
                        objParkingRequest.RequestedToDate = Convert.ToString(Obdt.Rows[i]["RequestedToDate"]);
                        objParkingRequest.RequestedFromTime = Convert.ToString(Obdt.Rows[i]["RequestedFromTime"]);
                        objParkingRequest.RequestedToTime = Convert.ToString(Obdt.Rows[i]["RequestedToTime"]);
                        objParkingRequest.Charges = Convert.ToDecimal(Obdt.Rows[i]["Amount"]);
                        objParkingRequest.TransactionFee = Convert.ToDecimal(Obdt.Rows[i]["TransactionFee"]);
                        objParkingRequest.Status = Convert.ToString(Obdt.Rows[i]["Status"]);
                        objParkingRequest.CarModel = Convert.ToString(Obdt.Rows[i]["CarModel"]);
                        objParkingRequest.CarColor = Convert.ToString(Obdt.Rows[i]["CarColor"]);
                        objParkingRequest.CarNumber = Convert.ToString(Obdt.Rows[i]["CarNumber"]);
                        objParkingRequest.PlaceName = Convert.ToString(Obdt.Rows[i]["PlaceName"]);
                        if (Convert.ToString(Obdt.Rows[i]["Address"]).Contains('\r'))
                        {
                            objParkingRequest.Address = Convert.ToString(Obdt.Rows[i]["Address"]);
                        }
                        else
                        {
                            objParkingRequest.Address = Convert.ToString(Obdt.Rows[i]["Address"]);
                        }
                        objParkingRequest.Latitude = Convert.ToString(Obdt.Rows[i]["Latitude"]);
                        objParkingRequest.Longitude = Convert.ToString(Obdt.Rows[i]["Longitude"]);
                        objParkingRequest.HasCancelled = Convert.ToBoolean(Obdt.Rows[i]["IsCancelled"]);
                        objParkingRequest.IsRated = Convert.ToBoolean(Obdt.Rows[i]["IsRated"]);
                        objParkingRequest.transactionNumber = Convert.ToString(Obdt.Rows[i]["transactionnumber"]);
                        objParkingRequest.canCancel = Convert.ToBoolean(Obdt.Rows[i]["canCancel"]);
                        objParkingRequest.PaymentStatus = Obdt.Rows[i]["PaymentStatus"].ToString();
                        objParkingRequest.OrderStatus = Obdt.Rows[i]["OrderStatus"].ToString();
                        objParkingRequest.isMultipleInOut = Convert.ToBoolean(Obdt.Rows[i]["isMultipleInOut"]);
                        objParkingRequest.multipleInOutAmount = Convert.ToDecimal(Obdt.Rows[i]["multipleInOutAmount"]);
                        objParkingRequest.multipleInOutHours = Convert.ToInt32(Obdt.Rows[i]["multipleInOutHours"]);

                        //status code 
                        objParkingRequest.isFreeHr = Convert.ToInt32(Obdt.Rows[i]["isFreeHr"]);
                        if (objParkingRequest.isFreeHr == 1)
                        {
                            DateTime additionalDateTime = Convert.ToDateTime(Convert.ToDateTime(Obdt.Rows[i]["FreeToDate"]).ToShortDateString() + " " + Obdt.Rows[i]["FreeToTime"]);
                            DateTime RequestedToDate = Convert.ToDateTime(objParkingRequest.RequestedToDate);
                            RequestedToDate = Convert.ToDateTime(RequestedToDate.ToShortDateString() + " " + objParkingRequest.RequestedToTime);
                            double diffhr = (additionalDateTime - RequestedToDate).TotalHours;
                            double hr = 0;
                            double min = 0;
                            if (diffhr > 0)
                            {
                                hr = Math.Floor(diffhr);
                                diffhr = diffhr - hr;
                                min = Math.Ceiling(diffhr * 60);
                                if (min >= 60)
                                {
                                    hr++;
                                    min = min - 60;
                                }
                                objParkingRequest.TotalFreeHrMin = hr + " hr " + min + " mins";
                            }
                            objParkingRequest.RequestedToDate = additionalDateTime.ToShortDateString();
                            objParkingRequest.RequestedToTime = additionalDateTime.ToShortTimeString();
                        }
                        TimeZone zone = TimeZone.CurrentTimeZone;
                        DateTime local = zone.ToLocalTime(DateTime.Now);
                        string fromdateNTime = Convert.ToDateTime(Obdt.Rows[i]["RequestedFromDate"]).ToShortDateString() + " " + Convert.ToDateTime(Obdt.Rows[i]["RequestedFromTime"]).ToShortTimeString();
                        string ToDateTime = Convert.ToDateTime(Obdt.Rows[i]["RequestedToDate"]).ToShortDateString() + " " + Convert.ToDateTime(Obdt.Rows[i]["RequestedToTime"]).ToShortTimeString();
                        TimeSpan timespan = (Convert.ToDateTime(fromdateNTime) - Convert.ToDateTime(local));
                        TimeSpan OrderStatustime = (Convert.ToDateTime(ToDateTime) - Convert.ToDateTime(local));

                        if (Convert.ToDecimal(Obdt.Rows[i]["cancelhours"]) >= 2)
                        {
                            objParkingRequest.IsCancelled = true;
                        }
                        else
                        {
                            objParkingRequest.IsCancelled = false;
                        }
                        var parkingrequestcanceldtl = (from parking in objContext.PT_ParkingRequests
                                                       where parking.Id == objParkingRequest.Id
                                                       select parking).FirstOrDefault();

                        DateTime parkingDtFrom = Convert.ToDateTime(Convert.ToDateTime(parkingrequestcanceldtl.RequestedFromDate).Date.ToString("MM/dd/yyyy") + " " + parkingrequestcanceldtl.RequestedFromTime);
                        if (parkingrequestcanceldtl != null && parkingrequestcanceldtl.refundAmount != null && parkingrequestcanceldtl.refundTransactionID != null)
                        {
                            objParkingRequest.IsRefunded = true;
                        }
                        else
                        {
                            objParkingRequest.IsRefunded = false;
                        }
                        if (parkingDtFrom >= DateTime.Now)
                        {
                            objParkingRequest.IsCancelTrue = true;
                        }
                        else
                        {
                            objParkingRequest.IsCancelTrue = false;
                        }

                        if (status == "Success")
                        {
                            ToEmail = Convert.ToString(Obdt.Rows[i]["Email"]);
                            Subject = "Transaction successfull";
                            body = "<p>Transaction successfull. " + SuccessMsg[1].ToString() + " </p>";
                        }
                        else if (status == "CancelRefundSuccess")
                        {
                            ToEmail = Convert.ToString(Obdt.Rows[i]["Email"]);
                            Subject = "Booking Refund & Cancellation Successfull";
                            body = "<p>Your booking has been cancelled & refunded successfully." + SuccessMsg[1].ToString() + " </p>";
                        }
                        else if (status == "ErrorCanRef")
                        {
                            ToEmail = Convert.ToString(Obdt.Rows[i]["Email"]);
                            Subject = "Error While Processing Cancellation Request";
                            body = "<p>Your Cancellation Request Could not be processed." + " </p>";
                        }
                        else if (status == "ErrorCancelRefundAlready")
                        {
                            ToEmail = Convert.ToString(Obdt.Rows[i]["Email"]);
                            Subject = "Booking Is Already Cancelled and Refunded";
                            body = "<p>Your booking has already been cancelled and refunded.</p>";
                        }
                        else if (status == "ErrorCancelAlready")
                        {
                            ToEmail = Convert.ToString(Obdt.Rows[i]["Email"]);
                            Subject = "Booking Is Already Cancelled";
                            body = "<p>Your booking has already been cancelled. </p>";
                        }
                        else if (status == "CancelSuccess")
                        {
                            ToEmail = Convert.ToString(Obdt.Rows[i]["Email"]);
                            Subject = "Booking Cancellation Successfull";
                            body = "<p>Your booking has been cancelled successfully." + SuccessMsg[1].ToString() + " </p>";
                        }
                        if (status != "Failed")
                        {
                            try
                            {
                                string Msg = body;
                                // SendOrdersEmail(ToEmail, Msg, Subject);
                            }

                            catch (Exception e)
                            {
                                ViewBag.test = e.InnerException.Message;
                                HandleError.ErrorMsg = e.InnerException.Message;
                            }
                        }
                        lstParkingRequest.Add(objParkingRequest);
                    }
                    ViewBag.ParkingId = objParkingRequest.ParkingId;
                    ViewBag.OrderStatus = "Processing";
                    return View(lstParkingRequest.OrderByDescending(m => m.Id));
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
                result = ex.ToString();
            }
            return View(lstParkingRequest);
        }
        public void SendOrdersEmail(String ToMail, String Msg, String Subject)
        {
            try
            {
                ListDictionary Tlst = new ListDictionary();
                //Send Cancellation mail to user 
                String ToEmailId = ToMail;
                //ToEmailId += "," + ConfigurationManager.AppSettings["MailIdForBookParkingSection"].ToString();
                //ToEmailId += "," + dtDetail.Rows[0]["EmailAddres"].ToString();//seller mailid
                String FromEmailId = "Parking-Nexus<" + ConfigurationManager.AppSettings["BookParking"].ToString() + ">";
                // String FromEmailId = ConfigurationManager.AppSettings["FromAddress"].ToString();
                String temp = HttpContext.Server.MapPath("~/Content/Template/Email/CommonMail.html");
                String SubjectLine = Subject;
                //String template = ConfigurationManager.AppSettings["EmailTemplate"].ToString() + temp;
                String template = temp;
                Tlst.Add("@@Msg", Msg);
                new AccountServiceManager().SendHTMLMail(FromEmailId, ToEmailId, SubjectLine, template, Tlst, "");
            }
            catch (Exception ex) { }
        }
        public ActionResult Myorder(string orderdetails = "")
        {
            string error = "Error";
            if (orderdetails.Contains(error))
            {
                ViewBag.error = "Some Error Occured";
            }
            else if (orderdetails != "")
            {
                ViewBag.error = "Transaction successfull";
            }
            return View();
        }
        #endregion

        #region Cancelparking
        //public ActionResult cancelparkingOld1(string TranxNo, string amount = "", int requestid = 0)
        //{
        //    try
        //    {
        //        if (requestid > 0)
        //        {
        //            decimal refAmount = Convert.ToDecimal(amount);
        //            PT_ParkingRequests objrequest = new PT_ParkingRequests();
        //            var requestdetails = (from details in objContext.PT_ParkingRequests
        //                                  where details.Id == requestid && details.transactionNumber == TranxNo && details.Amount == refAmount
        //                                  select details).FirstOrDefault();
        //            bool IsCancel = false;
        //            bool IsRefund = false;
        //            clsParkingRequest objParkingRequest = new clsParkingRequest();
        //            DataTable dtRequestDetail = new DataTable();
        //            objParkingRequest.RequestId = requestid;
        //            dtRequestDetail = objParkingRequest.GetParkingRequestDetailByRequestId();
        //            if (dtRequestDetail.Rows.Count > 0)
        //            {
        //                TimeZone zone = TimeZone.CurrentTimeZone;
        //                DateTime local = zone.ToLocalTime(DateTime.Now);
        //                string fromdateNTime = Convert.ToDateTime(dtRequestDetail.Rows[0]["RequestedFromDate"]).ToShortDateString() + " " + Convert.ToDateTime(dtRequestDetail.Rows[0]["RequestedFromTime"]).ToShortTimeString();
        //                TimeSpan timespan = (Convert.ToDateTime(fromdateNTime) - Convert.ToDateTime(local));
        //                // if time duration greater or equal to 2 then full refund else no refund
        //                if (Convert.ToInt32(timespan.Days) > 0)
        //                {
        //                    IsCancel = true;
        //                    IsRefund = true;
        //                }
        //                else
        //                {
        //                    if (Convert.ToInt32(timespan.Hours) >= 2)
        //                    {
        //                        IsCancel = true;
        //                        IsRefund = true;
        //                    }
        //                    else
        //                    {
        //                        IsCancel = true;
        //                        IsRefund = false;
        //                    }
        //                }
        //            }
        //            if (IsCancel && IsRefund)
        //            {
        //                if (requestdetails.IsCancelled != true && requestdetails.refundTransactionID == null)
        //                {
        //                    clsPayment objpayment = new clsPayment();
        //                    string result = objpayment.CancelWithPaypal(Convert.ToString(TranxNo), amount);
        //                    string msg = "Success";
        //                    if (result.Contains(msg))
        //                    {
        //                        string[] refundDetails = result.Split('-');
        //                        decimal refundAmount = Convert.ToDecimal(refundDetails[2]);
        //                        requestdetails.refundAmount = refundAmount;
        //                        requestdetails.refundTransactionID = refundDetails[1];
        //                        requestdetails.IsCancelled = true;
        //                        requestdetails.OrderStatus = "Completed";
        //                        objContext.Entry(requestdetails).State = EntityState.Modified;
        //                        objContext.SaveChanges();
        //                        objUtility.SendCancelParkingRequest(requestid);
        //                        objUtility.SendParkingRequestRefundMail(requestid);

        //                        return RedirectToAction("OrderDetails", new { orderdetails = "CancelRefund" });
        //                    }
        //                    else
        //                    {
        //                        return RedirectToAction("OrderDetails", new { orderdetails = "ErrorCanRef" });
        //                    }
        //                }
        //            }
        //            else if (IsCancel && !IsRefund)
        //            {
        //                if (requestdetails.IsCancelled != true && requestdetails.refundTransactionID == null)
        //                {
        //                    clsPayment objpayment = new clsPayment();
        //                    requestdetails.IsCancelled = true;
        //                    requestdetails.OrderStatus = "Completed";
        //                    objContext.Entry(requestdetails).State = EntityState.Modified;
        //                    objContext.SaveChanges();

        //                    //Send Cancellation mail to user,seller,admin
        //                    objUtility.SendCancelParkingRequest(requestid);

        //                    //end cancellation mail

        //                    return RedirectToAction("OrderDetails", new { orderdetails = "Cancel" });


        //                    //end cancellation mail

        //                }
        //                else
        //                {
        //                    if (requestdetails.IsCancelled == true && requestdetails.refundTransactionID != null)
        //                    {
        //                        return RedirectToAction("OrderDetails", new { orderdetails = "ErrorCancelRefundAlready" });
        //                    }
        //                    if (requestdetails.IsCancelled == true)
        //                    {
        //                        return RedirectToAction("OrderDetails", new { orderdetails = "ErrorCancelAlready" });
        //                    }
        //                    else
        //                    {
        //                        return RedirectToAction("OrderDetails", new { orderdetails = "ErrorCancelAlready" });
        //                    }
        //                }
        //            }
        //            else
        //            {
        //                if (requestdetails.Status != "Cancelled")
        //                {
        //                    clsPayment objpayment = new clsPayment();
        //                    requestdetails.IsCancelled = true;
        //                    objContext.Entry(requestdetails).State = EntityState.Modified;
        //                    objContext.SaveChanges();
        //                    return RedirectToAction("OrderDetails", new { orderdetails = "CancelSuccess" + "$" + "Success" });
        //                }
        //                else
        //                {
        //                    return RedirectToAction("OrderDetails", new { orderdetails = "ErrorCancelAlready" });
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        HandleError.ErrorMsg = ex.InnerException.Message;
        //        return RedirectToAction("Index", "Error");
        //    }
        //    return RedirectToAction("OrderDetails");
        //}
        /// <summary>
        /// make old on 13 jan 2017 after added new status "pandding" in request table and allow admin user to cancel a request from admin site
        /// </summary>
        /// <param name="TranxNo"></param>
        /// <param name="amount"></param>
        /// <param name="requestid"></param>
        /// <returns></returns>
        //public ActionResult cancelparkingOld(string TranxNo, string amount = "", int requestid = 0)
        //{
        //    try
        //    {
        //        if (requestid > 0)
        //        {
        //            decimal refAmount = Convert.ToDecimal(amount);
        //            PT_ParkingRequests objrequest = new PT_ParkingRequests();
        //            var requestdetails = (from details in objContext.PT_ParkingRequests
        //                                  where details.Id == requestid && details.transactionNumber == TranxNo && details.Amount == refAmount
        //                                  select details).FirstOrDefault();
        //            bool IsCancel = false;
        //            bool IsRefund = false;
        //            clsParkingRequest objParkingRequest = new clsParkingRequest();
        //            DataTable dtRequestDetail = new DataTable();
        //            objParkingRequest.RequestId = requestid;
        //            dtRequestDetail = objParkingRequest.GetParkingRequestDetailByRequestId();
        //            if (dtRequestDetail.Rows.Count > 0)
        //            {
        //                TimeZone zone = TimeZone.CurrentTimeZone;
        //                DateTime local = zone.ToLocalTime(DateTime.Now);
        //                string fromdateNTime = Convert.ToDateTime(dtRequestDetail.Rows[0]["RequestedFromDate"]).ToShortDateString() + " " + Convert.ToDateTime(dtRequestDetail.Rows[0]["RequestedFromTime"]).ToShortTimeString();
        //                TimeSpan timespan = (Convert.ToDateTime(fromdateNTime) - Convert.ToDateTime(local));

        //                // if time duration greater or equal to 2 then full refund else no refund
        //                if (Convert.ToInt32(timespan.Days) > 0)
        //                {
        //                    IsCancel = true;
        //                    IsRefund = true;
        //                }
        //                else
        //                {
        //                    if (Convert.ToInt32(timespan.Hours) >= 2)
        //                    {
        //                        IsCancel = true;
        //                        IsRefund = true;
        //                    }
        //                    else
        //                    {
        //                        IsCancel = true;
        //                        IsRefund = false;
        //                    }
        //                }
        //            }
        //            if (IsCancel && IsRefund)
        //            {
        //                if (requestdetails.IsCancelled != true && !string.IsNullOrWhiteSpace(requestdetails.refundTransactionID))
        //                {
        //                    clsPayment objpayment = new clsPayment();
        //                    var gateway = config.GetGateway();
        //                    Result<Transaction> resultN = gateway.Transaction.Refund(Convert.ToString(TranxNo));
        //                    if (!resultN.IsSuccess())
        //                    {
        //                        List<ValidationError> errors = resultN.Errors.DeepAll();
        //                    }
        //                    //string result = objpayment.CancelWithPaypal(Convert.ToString(TranxNo), amount);
        //                    string result = CancelWithBraintreeApi(TranxNo.ToString(), Convert.ToDecimal(amount));
        //                    string msg = "Success";
        //                    if (result.Contains(msg))
        //                    {
        //                        //string[] refundDetails = result.Split('-');
        //                        //decimal refundAmount = Convert.ToDecimal(refundDetails[2]);
        //                        requestdetails.refundAmount = Convert.ToDecimal(amount);
        //                        requestdetails.refundTransactionID = "";
        //                        requestdetails.IsCancelled = true;
        //                        requestdetails.OrderStatus = "Completed";
        //                        objContext.Entry(requestdetails).State = EntityState.Modified;
        //                        objContext.SaveChanges();

        //                        //Send Cancellation mail to user,seller,admin
        //                        ListDictionary Tlst1 = new ListDictionary();
        //                        String ToEmailId = dtRequestDetail.Rows[0]["UserEmail"].ToString();//UserEmail
        //                        ToEmailId += "," + ConfigurationManager.AppSettings["BookParking"].ToString();//AdminEmail
        //                        ToEmailId += "," + dtRequestDetail.Rows[0]["SellerEmail"].ToString();//SellerEmail
        //                        String FromEmailId = "Parking-Nexus<" + ConfigurationManager.AppSettings["BookParking"].ToString() + ">";
        //                        //String FromEmailId = ConfigurationManager.AppSettings["FromAddress"].ToString();

        //                        String temp1 = Server.MapPath("~/Content/Template/Email/CancelParkingRequest.html");
        //                        String SubjectLine1 = "Request is Cancelled";
        //                        String template1 = temp1;
        //                        // Tlst.Add("@@img", ConfigurationManager.AppSettings["ValetImagePathOnServer"].ToString() + Obdt.Rows[0]["Image"].ToString());
        //                        Tlst1.Add("", "");
        //                        try
        //                        {
        //                            new AccountServiceManager().SendHTMLMail(FromEmailId, ToEmailId, SubjectLine1, template1, Tlst1, "");
        //                        }
        //                        catch (Exception ex)
        //                        {

        //                            throw;
        //                        }

        //                        //end cancellation mail
        //                        //Send Refund mail to user,seller,admin
        //                        ListDictionary Tlst2 = new ListDictionary();
        //                        String temp2 = HttpContext.Server.MapPath("~/Content/Template/Email/RefundMail.html");
        //                        // String temp2 = "Templates\\RefundMail.html";
        //                        String SubjectLine2 = "Refund mail";
        //                        String template2 = temp2;
        //                        // Tlst.Add("@@img", ConfigurationManager.AppSettings["ValetImagePathOnServer"].ToString() + Obdt.Rows[0]["Image"].ToString());
        //                        Tlst2.Add("", "");
        //                        try
        //                        {
        //                            new AccountServiceManager().SendHTMLMail(FromEmailId, ToEmailId, SubjectLine2, template2, Tlst2, "");
        //                        }
        //                        catch (Exception ex)
        //                        {

        //                            throw;
        //                        }
        //                        return RedirectToAction("OrderDetails", new { orderdetails = "CancelRefund" });
        //                    }
        //                    else
        //                    {
        //                        return RedirectToAction("OrderDetails", new { orderdetails = "ErrorCanRef" });
        //                    }
        //                }
        //            }
        //            else if (IsCancel && !IsRefund)
        //            {
        //                if (requestdetails.IsCancelled != true)
        //                {
        //                    clsPayment objpayment = new clsPayment();
        //                    requestdetails.IsCancelled = true;
        //                    requestdetails.OrderStatus = "Completed";
        //                    objContext.Entry(requestdetails).State = EntityState.Modified;
        //                    objContext.SaveChanges();

        //                    //Send Cancellation mail to user,seller,admin
        //                    ListDictionary Tlst1 = new ListDictionary();
        //                    String ToEmailId = dtRequestDetail.Rows[0]["UserEmail"].ToString();
        //                    ToEmailId += "," + ConfigurationManager.AppSettings["BookParking"].ToString();
        //                    ToEmailId += "," + dtRequestDetail.Rows[0]["SellerEmail"].ToString();
        //                    String FromEmailId = "Parking-Nexus<" + ConfigurationManager.AppSettings["BookParking"].ToString() + ">";
        //                    // String FromEmailId = ConfigurationManager.AppSettings["FromAddress"].ToString();

        //                    String temp1 = Server.MapPath("~/Content/Template/Email/CancelParkingRequest.html");
        //                    String SubjectLine1 = "Request is Cancelled";
        //                    String template1 = temp1;
        //                    // Tlst.Add("@@img", ConfigurationManager.AppSettings["ValetImagePathOnServer"].ToString() + Obdt.Rows[0]["Image"].ToString());
        //                    Tlst1.Add("", "");
        //                    try
        //                    {
        //                        new AccountServiceManager().SendHTMLMail(FromEmailId, ToEmailId, SubjectLine1, template1, Tlst1, "");
        //                    }
        //                    catch (Exception ex)
        //                    {

        //                        throw;
        //                    }

        //                    //end cancellation mail

        //                    return RedirectToAction("OrderDetails", new { orderdetails = "Cancel" });
        //                    //end cancellation mail
        //                }
        //                else
        //                {
        //                    if (requestdetails.IsCancelled == true && requestdetails.refundTransactionID != null)
        //                    {
        //                        return RedirectToAction("OrderDetails", new { orderdetails = "ErrorCancelRefundAlready" });
        //                    }
        //                    if (requestdetails.IsCancelled == true)
        //                    {
        //                        return RedirectToAction("OrderDetails", new { orderdetails = "ErrorCancelAlready" });
        //                    }
        //                    else
        //                    {
        //                        return RedirectToAction("OrderDetails", new { orderdetails = "ErrorCancelAlready" });
        //                    }
        //                }
        //            }
        //            else
        //            {
        //                if (requestdetails.Status != "Cancelled")
        //                {
        //                    clsPayment objpayment = new clsPayment();
        //                    requestdetails.IsCancelled = true;
        //                    objContext.Entry(requestdetails).State = EntityState.Modified;
        //                    objContext.SaveChanges();
        //                    return RedirectToAction("OrderDetails", new { orderdetails = "CancelSuccess" + "$" + "Success" });
        //                }
        //                else
        //                {
        //                    return RedirectToAction("OrderDetails", new { orderdetails = "ErrorCancelAlready" });
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        HandleError.ErrorMsg = ex.InnerException.Message;
        //        return RedirectToAction("Index", "Error");
        //    }
        //    return RedirectToAction("OrderDetails");
        //}


        public ActionResult cancelparking(string TranxNo = "", string amount = "", int requestid = 0)
        {

            if (requestid > 0)
            {
                PT_ParkingRequests objrequest = new PT_ParkingRequests();
                DataTable dtRequestDetail = new DataTable();
                objParkingRequest.RequestId = requestid;
                dtRequestDetail = objParkingRequest.GetParkingRequestDetailByRequestId();
                try
                {
                    var requestdetails = (from details in objContext.PT_ParkingRequests
                                          where details.Id == requestid
                                          select details).FirstOrDefault();
                    if (requestdetails != null && requestdetails.IsCancelled != true && dtRequestDetail.Rows.Count > 0)
                    {
                        TimeSpan ts = TimeSpan.Parse(requestdetails.RequestDateTimeOffSet.Replace("+", ""));
                        DateTime nowDate = DateTime.UtcNow.Add(ts);
                        DateTime requestDateTime = Convert.ToDateTime(requestdetails.RequestedFromDate);
                        requestDateTime = Convert.ToDateTime(requestDateTime.ToShortDateString() + " " + requestdetails.RequestedFromTime);
                        bool canRefund = (requestDateTime - nowDate).TotalHours >= 2 ? true : false;
                        if (canRefund)
                        {
                            List<PT_PaymentTransaction> paymentList = objContext.PT_PaymentTransaction.Where(x => x.RequestID == requestid && x.RequestType == "Parking").ToList();

                            List<PT_PaymentTransaction> ccPAymentList = paymentList.Where(x => x.TransactionType == "Credit Card").ToList();
                            if (ccPAymentList != null && ccPAymentList.Count > 0)
                            {
                                foreach (PT_PaymentTransaction transaction in ccPAymentList)
                                {
                                    decimal refundTotal = (transaction.RefundAmount1 ?? 0) + (transaction.RefundAmount2 ?? 0) + (transaction.RefundAmount3 ?? 0);
                                    if (transaction.Amount > refundTotal)
                                    {
                                        clsPayment objpayment = new clsPayment();
                                        var gateway = config.GetGateway();
                                        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                                        Result<Transaction> resultN = gateway.Transaction.Refund(Convert.ToString(transaction.TransactionID));
                                        if (!resultN.IsSuccess())
                                        {

                                            var result = CancelWithBraintreeApi(TranxNo.ToString(), Convert.ToDecimal(amount));
                                            if (string.IsNullOrWhiteSpace(transaction.RefundID1) && string.IsNullOrWhiteSpace(transaction.RefundStatus1))
                                            {
                                                transaction.RefundID1 = transaction.TransactionID;
                                                transaction.RefundAmount1 = transaction.Amount;
                                                transaction.RefundStatus1 = "full";
                                                transaction.RefundType1 = "Credit Card";
                                            }
                                            else if (string.IsNullOrWhiteSpace(transaction.RefundID2) && string.IsNullOrWhiteSpace(transaction.RefundStatus2))
                                            {
                                                transaction.RefundID2 = transaction.TransactionID;
                                                transaction.RefundAmount2 = transaction.Amount;
                                                transaction.RefundStatus2 = "Paid";
                                                transaction.RefundStatus2 = "Part";
                                                transaction.RefundType2 = "Credit Card";
                                            }
                                            else if (string.IsNullOrWhiteSpace(transaction.RefundID3) && string.IsNullOrWhiteSpace(transaction.RefundStatus3))
                                            {
                                                transaction.RefundID3 = transaction.TransactionID;
                                                transaction.RefundAmount3 = transaction.Amount;
                                                transaction.RefundStatus3 = "Paid";
                                                transaction.RefundStatus3 = "Part";
                                                transaction.RefundType3 = "Credit Card";
                                            }
                                        }
                                        else
                                        {
                                            if (string.IsNullOrWhiteSpace(transaction.RefundID1) && string.IsNullOrWhiteSpace(transaction.RefundStatus1))
                                            {
                                                transaction.RefundID1 = resultN.Target.Id;
                                                transaction.RefundAmount1 = transaction.Amount;
                                                transaction.RefundStatus1 = "Full";
                                            }
                                            else if (string.IsNullOrWhiteSpace(transaction.RefundID2) && string.IsNullOrWhiteSpace(transaction.RefundStatus2))
                                            {
                                                transaction.RefundID2 = resultN.Transaction.Id;
                                                transaction.RefundAmount2 = transaction.Amount;
                                                transaction.RefundStatus2 = "Part";
                                            }
                                            else if (string.IsNullOrWhiteSpace(transaction.RefundID3) && string.IsNullOrWhiteSpace(transaction.RefundStatus3))
                                            {
                                                transaction.RefundID3 = resultN.Transaction.Id;
                                                transaction.RefundAmount3 = transaction.Amount;
                                                transaction.RefundStatus3 = "Part";
                                            }
                                        }
                                    }

                                }
                                requestdetails.refundAmount = Convert.ToDecimal(amount);
                                requestdetails.refundTransactionID = "";
                                if (ccPAymentList != null)
                                {
                                    decimal totalRefund = ccPAymentList.Sum(x => (Convert.ToDecimal(x.RefundAmount1) + Convert.ToDecimal(x.RefundAmount2) + Convert.ToDecimal(x.RefundAmount3)));
                                    decimal totalPaid = paymentList.Sum(x => Convert.ToDecimal(x.Amount));
                                    if (totalRefund == totalPaid)
                                    {
                                        requestdetails.Status = "Full Refund";
                                    }
                                    else
                                    {
                                        requestdetails.Status = "Partial Refund";
                                    }
                                }
                                requestdetails.IsCancelled = true;
                                requestdetails.OrderStatus = "Completed";
                                objContext.Entry(requestdetails).State = EntityState.Modified;
                                objContext.SaveChanges();
                                //Send Refund mail to user,seller,admin
                                //  objUtility.SendParkingRequestRefundMail(requestid);
                                //Send Cancellation mail to user,seller,admin
                                //objUtility.SendCancelParkingRequest(requestid);
                                //end cancellation mail
                                objUtility.SendParkingEmailUpdate(requestid, "refund");
                                return RedirectToAction("OrderDetails", new { orderdetails = "CancelRefund", requestid = requestid });
                            }
                            else
                            {
                                requestdetails.IsCancelled = true;
                                requestdetails.OrderStatus = "Completed";
                                objContext.Entry(requestdetails).State = EntityState.Modified;
                                objContext.SaveChanges();
                                //Send Cancellation mail to user,seller,admin
                                // objUtility.SendCancelParkingRequest(requestid);
                                objUtility.SendParkingEmailUpdate(requestid, "cancel");
                                return RedirectToAction("OrderDetails", new { orderdetails = "Cancel", requestid = requestid });
                            }
                        }
                        else
                        {
                            requestdetails.IsCancelled = true;
                            requestdetails.OrderStatus = "Completed";
                            objContext.Entry(requestdetails).State = EntityState.Modified;
                            objContext.SaveChanges();
                            //Send Cancellation mail to user,seller,admin
                            //  objUtility.SendCancelParkingRequest(requestid);
                            objUtility.SendParkingEmailUpdate(requestid, "cancel");
                            return RedirectToAction("OrderDetails", new { orderdetails = "Cancel", requestid = requestid });
                            //end cancellation mail
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.Write(ex.Message);
                }
            }
            return RedirectToAction("OrderDetails", new { requestid = requestid });
        }


        /// <summary>
        /// Cancel Transaction using Braintree Api
        /// </summary>
        /// <param name="transactionID"></param>
        /// <param name="RefundAmount"></param>
        /// <returns></returns>
        public Result<Transaction> CancelWithBraintreeApi(string transactionID, decimal RefundAmount)
        {

            try
            {
                var gateway = config.GetGateway();
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                // When the transaction is voided, Braintree will perform an authorization reversal, if possible, to remove the pending charge from the customer's card.
                return gateway.Transaction.Void(transactionID);


                /**
                 * code for sattelment
                Transaction transaction = gateway.Transaction.Find(transactionID);
                if (transaction.Status.ToString() == "settled" || transaction.Status.ToString() == "settling")
                {

                    //refund transaction now
                    Result<Transaction> resultR = gateway.Transaction.Refund(
                                                transactionID, RefundAmount);
                    var transId = resultR.Transaction;
                    Message = "Success-" + transId;
                }
                else if (transaction.Status.ToString() == "submitted_for_settlement")
                {
                    // Already submitted_for_settlement
                    Message = "Already submitted for settlement";
                }
                else
                {
                    //submit for settlement
                    Result<Transaction> resultS = gateway.Transaction.SubmitForSettlement(transactionID);

                    if (resultS.IsSuccess())
                    {
                        Transaction settledTransaction = resultS.Target;
                        //Console.WriteLine(transaction.Type);   // TransactionType.CREDIT
                        //Console.WriteLine(settledTransaction.Status); // TransactionStatus.SUBMITTED_FOR_SETTLEMENT
                        var Status = settledTransaction.Status.ToString();
                        if (Status.ToString() == "submitted_for_settlement")
                        {
                            Message = "Submit for settlement";
                        }
                        else
                        {
                            Message = Status.ToString();
                        }

                    }
                }
        **/
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write(ex.Message);
                // do something to catch the error, like write to a log file.
                // Response.Write(“error processing”);
                //  Response.End();

            }
            return null;
        }

        public ActionResult cancelsubscription(string TranxNo = "", string stop = "")
        {
            if (!string.IsNullOrWhiteSpace(TranxNo))
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var gateway = config.CreateGateway();
                try
                {
                    if (stop.ToLower() == "stop")
                    {
                        var result = gateway.Subscription.Update(TranxNo, new SubscriptionRequest { NumberOfBillingCycles = 1 });
                        if (result.IsSuccess())
                        {
                            updateSubscriptionDetailsToCancel(TranxNo);
                            System.Threading.Thread.Sleep(2000);
                        }
                        else if (result.Errors != null)
                        {

                            ValidationErrors vE = result.Errors;
                            if (vE != null && vE.DeepCount > 0)
                            {
                                if (vE.DeepAll().Any(x => x.Code == ValidationErrorCode.SUBSCRIPTION_STATUS_IS_CANCELED))
                                {
                                    updateSubscriptionDetailsToCancel(TranxNo);
                                }
                            }
                        }

                    }
                    else
                    {
                        var result = gateway.Subscription.Cancel(TranxNo);
                        if (result.IsSuccess())
                        {
                            System.Threading.Thread.Sleep(2000);
                        }
                        else if (result.Errors != null)
                        {
                            ValidationErrors vE = result.Errors;
                            if (vE != null && vE.DeepCount > 0)
                            {
                                if (vE.DeepAll().Any(x => x.Code == ValidationErrorCode.SUBSCRIPTION_STATUS_IS_CANCELED))
                                {
                                    updateSubscriptionDetailsToCancel(TranxNo);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.Write(ex.Message);
                }
            }
            return RedirectToAction("subscriptionDetails", new { reqid = TranxNo });
        }

        private void updateSubscriptionDetailsToCancel(object subscriptionId)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region AddNewcar
        public int addNewcar(string carModel, string PlateNo, string color)
        {
            int carid = 0;
            int UserId = Convert.ToInt32(Session["userID"]);
            if (UserId == 0)
            {
                UserId = Convert.ToInt32(Session["CheckoutID"]);
            }
            //PT_CarDetail objnewcar = new PT_CarDetail()
            //{
            //    UserId = UserId,
            //    CarColor = color,
            //    CarModel = carModel,
            //    CarNumber = PlateNo,
            //    SaveForFuture = "T",
            //    Status = "A",
            //};
            //objContext.PT_CarDetail.Add(objnewcar);
            //objContext.SaveChanges();
            VehicleViewModel.AddVehicle objVeicleInfo = new VehicleViewModel.AddVehicle();
            objVeicleInfo.UserId = UserId;
            objVeicleInfo.CarColor = color;
            objVeicleInfo.CarModel = carModel;
            objVeicleInfo.CarNumber = PlateNo;
            objVeicleInfo.SaveForFuture = "T";

            int result = new VehicleServiceManager().InsertCarDetails(objVeicleInfo);
            carid = result;
            return carid;
        }
        public int UpdateCar(VehicleViewModel.AddVehicle objVeicleInfo)
        {
            int returnvalue = 0;
            if (Convert.ToString(Session["userID"]) != null && Convert.ToString(Session["userID"]) != "")
            {
                objVeicleInfo.UserId = Convert.ToInt32(Session["userID"]);
            }
            else
            {
                objVeicleInfo.UserId = Convert.ToInt32(Session["CheckoutID"]);
            }
            int result = new VehicleServiceManager().UpdatetCarDetails(objVeicleInfo);
            if (result > 0)
            {
                returnvalue = 1;
            }
            return returnvalue;
        }
        public int DeleteCar(int DeletecarID)
        {
            int returnvalue = 0;
            if (DeletecarID > 0)
            {
                bool result = new VehicleServiceManager().DeleteCar(DeletecarID);
                if (result)
                {
                    returnvalue = 1;
                }
            }
            return returnvalue;
        }
        public ActionResult AddNewComent(string parkingID, string RequestID, string title, string comment, string rating)
        {
            int userid = Convert.ToInt32(Session["userid"]);
            if (userid > 0)
            {

            }
            else
            {
                userid = Convert.ToInt32(Session["CheckoutID"]);
            }
            bool result = false;
            int parkID = Convert.ToInt32(parkingID);
            int ReqID = Convert.ToInt32(RequestID);
            string createdby = Convert.ToString(userid);
            var reviewByUser = (from review in objContext.PT_Feedback
                                where review.ParkingId == parkID && review.CreatedBy == createdby && review.RequestID == ReqID
                                select review).FirstOrDefault();
            ObjectParameter objOutput = new ObjectParameter("ReturnValue", typeof(int));
            if (reviewByUser != null)
            {
                objContext.PT_ADD_UPDATE_FEEDBACK(reviewByUser.Id, reviewByUser.ParkingId, reviewByUser.RequestID, title, comment, rating, createdby, objOutput);
            }
            else
            {
                objContext.PT_ADD_UPDATE_FEEDBACK(0, parkID, ReqID, title, comment, rating, createdby, objOutput);
            }
            result = true;
            return Json(result, JsonRequestBehavior.AllowGet);
        }
        #endregion

        #region CheckOutGuest
        //},
        //public string CheckOutGuest(string FirstName, string LastName, string Email, string CountryID, string StateID, string CityID, string Address, string Contact, string Zipcode)
        [HttpPost]
        public string CheckOutGuest(string FirstName, string LastName, string Email, string CountryID, string StateID, string CityID, string Address, string Contact, string Zipcode, int parkingid, string fromTime, string toTime, string fromDate, string toDate, string totalHous, string price, string parkingtax, string Unitprice, string TransFee, string loginopt = "", decimal TotalSpecialCharges = 0, int monthly = 0, int endday = 0, string freeHoursUpTo = "")
        {
            CheclOutuserLogin objcheckuser = new CheclOutuserLogin();
            string output = "";
            int result = 0;
            AccountViewModel.SignUpModel objSignUpModel = new AccountViewModel.SignUpModel()
            {
                FirstName = FirstName,
                LastName = LastName,
                Email = Email,
                CountryID = Convert.ToInt32(CountryID != "" ? CountryID : "0"),
                StateID = Convert.ToInt32(StateID != "" ? StateID : "0"),
                CityID = Convert.ToInt32(CityID != "" ? CityID : "0"),
                Address = Address,
                Contact = Contact,
                ZipCode = Zipcode,
                UserType = "1"
            };
            result = new AccountServiceManager().GuestCheckout(objSignUpModel);
            if (result > 0)
            {
                Session["CheckoutID"] = result;
                //bool Result = BookParking(parkingid, FromTime, ToTime, FromDate, ToDate, totalHous, ParkingRate, parkingtax, Unitprice, TransFee, "Guest", TotalSpecialCharges, monthly, endday);
                bool Result = BookParking(parkingid, fromTime, toTime, fromDate, toDate, totalHous, price, parkingtax, Unitprice, TransFee, "Guest", TotalSpecialCharges, monthly, endday, freeHoursUpTo: freeHoursUpTo);
                int checkpassword = objcheckuser.Checkpasswordbyuserid(Email);
                if (checkpassword == 2)
                {
                    return "2";
                }
                TempData["CheckOutEmail"] = Email;
                output = "-3";
            }
            else if (result == -3)
            {
                //bool Result = BookParking(ParkId, FromTime, ToTime, FromDate, ToDate, totalHous, ParkingRate, "", "", "", "Guest", TotalSpecialCharges, monthly, endday);
                bool Result = BookParking(ParkId, fromTime, toTime, fromDate, toDate, totalHous, price, "", "", "", "Guest", TotalSpecialCharges, monthly, endday, freeHoursUpTo: freeHoursUpTo);
                int checkpassword = objcheckuser.Checkpasswordbyuserid(Email);
                if (checkpassword == 2)
                {
                    return "2";
                }
                TempData["CheckOutEmail"] = Email;
                output = "-3";
            }
            return output;
        }
        #endregion

        public ActionResult GetAmenties(int ParkingId)
        {
            DataTable dtAmenities = new DataTable();
            clsParking obj = new clsParking();
            clsAmenities objAmenities = new clsAmenities();
            List<clsAmenities> lstclsAmenities = new List<clsAmenities>();
            dtAmenities = obj.GetAmenitiesByParkingsId(ParkingId);
            if (dtAmenities.Rows.Count > 0)
            {
                for (int i = 0; i < dtAmenities.Rows.Count; i++)
                {
                    objAmenities = new clsAmenities();
                    objAmenities.AmenitiesId = Convert.ToInt32(dtAmenities.Rows[i]["AMENITY_ID"]);
                    objAmenities.AmenityName = Convert.ToString(dtAmenities.Rows[i]["AMENITY_NAME"]);
                    lstclsAmenities.Add(objAmenities);
                }
            }
            return PartialView("_ParkingAmenities", lstclsAmenities);
        }
        public ActionResult GetParkingInstructions(int ParkingId)
        {
            DataTable dtParking = new DataTable();
            clsParking obj = new clsParking();
            List<clsParking> lstclsParking = new List<clsParking>();
            dtParking = obj.GetParkingByParkingId(ParkingId);
            if (dtParking.Rows.Count > 0)
            {
                for (int i = 0; i < dtParking.Rows.Count; i++)
                {
                    obj = new clsParking();
                    obj.Instruction = Convert.ToString(dtParking.Rows[i]["Instructions"]);
                    lstclsParking.Add(obj);
                }
            }
            return PartialView("_ParkingInstructions", lstclsParking);
        }
        public ActionResult GetParkingImages(int ParkingId)
        {
            DataTable dtParking = new DataTable();
            clsParking obj = new clsParking();
            List<clsParking> lstclsParking = new List<clsParking>();
            dtParking = obj.GetParkingImagesByParkingId(ParkingId);
            if (dtParking.Rows.Count > 0)
            {
                for (int i = 0; i < dtParking.Rows.Count; i++)
                {
                    obj = new clsParking();
                    obj.Image = Convert.ToString(dtParking.Rows[i]["ImageName"]);
                    lstclsParking.Add(obj);
                }
            }
            return PartialView("_TopSlider", lstclsParking);
        }
        public ActionResult GetParkingThunmbNailImages(int ParkingId)
        {
            DataTable dtParking = new DataTable();
            clsParking obj = new clsParking();
            List<clsParking> lstclsParking = new List<clsParking>();
            dtParking = obj.GetParkingImagesByParkingId(ParkingId);
            if (dtParking.Rows.Count > 0)
            {
                for (int i = 0; i < dtParking.Rows.Count; i++)
                {
                    obj = new clsParking();
                    obj.Image = Convert.ToString(dtParking.Rows[i]["ImageName"]);
                    lstclsParking.Add(obj);
                }
            }
            return PartialView("_BottomSlider", lstclsParking);
        }

        public ActionResult GetCommentByUser(int? parkingID, int? RequestId)
        {
            string LoginUserID = Convert.ToString(Session["userid"]);
            if (LoginUserID != null && LoginUserID != "")
            {
                var UserComment = (from feedback in objContext.PT_Feedback
                                   where feedback.ParkingId == parkingID && feedback.CreatedBy == LoginUserID
                                    && feedback.RequestID == RequestId
                                   select feedback).FirstOrDefault();
                if (UserComment != null)
                {
                    string ReviewData = Convert.ToInt32(UserComment.Rating) + "_" + UserComment.Title + "_" + Server.UrlDecode(UserComment.Comments);
                    return Json(ReviewData, JsonRequestBehavior.AllowGet);
                }
            }
            return Json(false, JsonRequestBehavior.AllowGet);
        }
        public string RenderRazorViewToString(string viewName, object model)
        {
            try
            {
                ViewData.Model = model;
                using (var sw = new System.IO.StringWriter())
                {
                    var viewResult = ViewEngines.Engines.FindPartialView(ControllerContext, viewName);
                    var viewContext = new ViewContext(ControllerContext, viewResult.View, ViewData, TempData, sw);
                    viewResult.ViewEngine.ReleaseView(ControllerContext, viewResult.View);
                    viewResult.View.Render(viewContext, sw);
                    return Convert.ToString(sw.GetStringBuilder());
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        public ActionResult parkingDurationEditByParkingId(int parkingID, string _BookingFromDateTime, string _BookingToDateTime, string latLong, int monthly = 0, int endday = 0, int isMultipleInOut = 0, int multipleInOutOption = 0)
        {
            ParkingNexusBLL.clsSearchLocation objclsSearchLocation = new ParkingNexusBLL.clsSearchLocation();
            clsDateHelper dateHelper = new clsDateHelper();
            ParkingNexusBLL.clsParkingBookingModal result = new ParkingNexusBLL.clsParkingBookingModal();
            DateTime BookingFromDateTime, BookingToDateTime;
            BookingFromDateTime = Convert.ToDateTime(_BookingFromDateTime);
            BookingToDateTime = Convert.ToDateTime(_BookingToDateTime);
            if (monthly != 1)
            {
                dateHelper.verfiySearchDateTime(_BookingFromDateTime, _BookingToDateTime, out BookingFromDateTime, out BookingToDateTime);
            }
            else
            {
                BookingFromDateTime = Convert.ToDateTime(BookingFromDateTime.ToShortDateString());
                if (BookingFromDateTime.Date < DateTime.Now.Date)
                {
                    BookingFromDateTime = Convert.ToDateTime(DateTime.Now.ToShortDateString());
                }
                if (monthly == 1 && endday != 1)
                {
                    BookingToDateTime = BookingFromDateTime.AddMonths(1).AddMinutes(-1);
                }
                else
                {
                    BookingToDateTime = Convert.ToDateTime(BookingToDateTime.ToShortDateString()).AddDays(1).AddMinutes(-1);
                }
            }
            double totalMonths = 0;
            if (monthly == 1)
            {
                totalMonths = DateTimeHelper.MonthDifference(BookingFromDateTime, BookingToDateTime);
                if (totalMonths < 1)
                {
                    BookingToDateTime = BookingFromDateTime.AddMonths(1).AddMinutes(-1);
                    totalMonths = DateTimeHelper.MonthDifference(BookingFromDateTime, BookingToDateTime);
                }
            }
            decimal unitPrice = 0.0M;
            string DayIds = string.Empty;
            result.parkingID = parkingID;
            result.BookingDateFrom = BookingFromDateTime.ToString();
            result.BookingDateTo = BookingToDateTime.ToString();
            DayIds = dateHelper.getDayIds(BookingFromDateTime, BookingToDateTime);
            TimeSpan diff = BookingToDateTime - BookingFromDateTime;
            decimal RequestedBookingHr = Math.Round(Convert.ToDecimal((diff).TotalHours), 2);
            ParkingNexusBLL.clsParking obj = new ParkingNexusBLL.clsParking();
            try
            {
                if (RequestedBookingHr > 0)
                {
                    if (monthly == 1 || obj.CheckParkingAvailableOrNot("", parkingID, BookingFromDateTime.ToString(), BookingToDateTime.ToString()) == 1)
                    {
                        bool applyOnlySpecial = false;
                        clsParking objParking = new clsParking();
                        objParking.Id = Convert.ToInt32(parkingID);
                        decimal ParkingTotalCharges = 0.0M;
                        decimal UnitRate = 0.0M;
                        #region break booking date in parts for sepcial pricing and calculate part charges
                        if (monthly == 1)
                        {
                            Dictionary<string, object> dt = objclsSearchLocation.getParkingMonthlyCharge(BookingFromDateTime, BookingToDateTime, objParking.Id, getLoginUserEmail(), (endday == 1 && (BookingToDateTime - BookingFromDateTime).TotalDays <= 60));
                            ParkingTotalCharges = Math.Round(Convert.ToDecimal(dt["TotalCharge"]), 2);
                            objParking.Description = Convert.ToString(dt["Description"]);
                            objParking.Instruction = Convert.ToString(dt["Instructions"]);
                        }
                        else
                        {
                            Dictionary<string, object> rateResult = objclsSearchLocation.getspecialPrice(BookingFromDateTime, BookingToDateTime, parkingID, out applyOnlySpecial);
                            objParking.TotalSpecialCharges = Convert.ToDecimal(rateResult["totalCharge"]);
                            if (objParking.TotalSpecialCharges > 0)
                            {
                                result.freehrUpTo = rateResult["freehrUpTo"].ToString();
                                ParkingTotalCharges += objParking.TotalSpecialCharges;

                            }
                            if (!applyOnlySpecial)
                            {
                                rateResult = objclsSearchLocation.CalculateChargesNew1(parkingID.ToString(), BookingFromDateTime.ToString(), BookingToDateTime.ToString(), 1, "", RequestedBookingHr, out UnitRate);
                                result.freehrUpTo = rateResult["freehrUpTo"].ToString();
                                ParkingTotalCharges += Convert.ToDecimal(rateResult["totalCharge"]);
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(result.freehrUpTo))
                        {
                            DateTime d = Convert.ToDateTime(BookingToDateTime);
                            DateTime d2 = Convert.ToDateTime(result.freehrUpTo);
                            if ((d2 - d).TotalMinutes <= 0)
                            {
                                result.freehrUpTo = "";
                            }
                            else
                            {
                                DateTime NextDayStart = Convert.ToDateTime(d2.ToShortDateString()).AddDays(1);
                                if ((NextDayStart - d2).TotalMinutes <= 1)
                                {
                                    d2 = NextDayStart;
                                }
                            }
                            result.freehrUpTo = d2.ToString();
                            ParkingNexusBLL.clsParking objCheck = new ParkingNexusBLL.clsParking();
                            if (objCheck.CheckParkingAvailableOrNot("", objParking.Id, BookingFromDateTime.ToString(), d2.ToString()) == 1)
                            {
                                result.freehrUpTo = d2.ToString();
                            }
                            else
                            {
                                result.freehrUpTo = "";
                            }
                        }
                        #endregion
                        #region if parking has some charges than add it to search list
                        if (ParkingTotalCharges > 0)
                        {

                            result.Unitprice = unitPrice;
                            result.TransFee = objclsSearchLocation.GetTransactionFee(BookingFromDateTime.ToString(), BookingToDateTime.ToString());
                            var MultipleInOutRateInfo = (from MultipleInOutRates in objContext.PK_ParkingMultipleInOutRate
                                                         where MultipleInOutRates.parkingId == parkingID && MultipleInOutRates.Id == multipleInOutOption
                                                         select MultipleInOutRates).OrderBy(x => x.Hours).FirstOrDefault();
                            decimal MultipleInOutRate = 0.0m;
                            if (MultipleInOutRateInfo != null)
                            {
                                MultipleInOutRate = Convert.ToDecimal(MultipleInOutRateInfo.Rate);
                            }
                            #region Parking Fee
                            decimal TotalAmount = ParkingTotalCharges + (isMultipleInOut == 1 ? MultipleInOutRate : 0m);
                            decimal Amount = 0m;
                            decimal ParkingTax = 0m;
                            decimal ConvenienceTax = 0m;
                            decimal AmountWithoutConvenienceFee = 0m;
                            ConvenienceTax = 0.00m;
                            result.Price = ParkingTotalCharges;
                            result.TotalAmount = TotalAmount;
                            result.TotalSpecialCharges = objParking.TotalSpecialCharges;
                            AmountWithoutConvenienceFee = (Convert.ToDecimal(TotalAmount) - ConvenienceTax);
                            Amount = AmountWithoutConvenienceFee * Convert.ToDecimal(0.8);
                            ParkingTax = AmountWithoutConvenienceFee - Amount;
                            #endregion
                            result.Price += result.TransFee;
                            result.TotalAmount += result.TransFee;
                            result.parkingtax = ParkingTax;
                            result.loginopt = "";
                            result.isAvailable = true;
                        }
                        else
                        {
                            result.ErrorMsg = "Parking is not Available. Please try with other date time range.";
                            result.isAvailable = false;
                        }
                        #endregion
                    }
                    else
                    {
                        result.ErrorMsg = "Parking is not Available. Please try with other date time range.";
                        result.isAvailable = false;
                    }
                }
                else
                {
                    result.ErrorMsg = "Please select a valid date time range.";
                    result.isAvailable = false;
                }
            }
            catch (Exception ex)
            {
                result.ErrorMsg = ex.Message;
                result.isAvailable = false;
            }
            result.totalHours = Math.Truncate(diff.TotalMinutes / 60) + "hr " + diff.TotalMinutes % 60 + "mins";
            if (!string.IsNullOrWhiteSpace(result.freehrUpTo))
            {
                DateTime d = Convert.ToDateTime(result.BookingDateTo);
                DateTime d2 = Convert.ToDateTime(result.freehrUpTo);
                if ((d2 - d).TotalMinutes <= 0)
                {
                    result.freehrUpTo = "";
                }
            }
            if (!result.isAvailable)
            {
                if (latLong == null)
                {
                    latLong = Session["Latitude"] != null ? Session["Latitude"].ToString() : "";
                }
                result.suggestionList = new SearchController().getParkingsWithInParticularDistanceAndDuration(latLong, BookingFromDateTime.ToString(), BookingToDateTime.ToString(), monthly);
            }
            return Json(result);
        }
        protected string getLoginUserEmail()
        {
            string userEmail = "";
            if (Session["userID"] != null)
            {
                int UserId = Convert.ToInt32(Session["userID"]);
                BAL.PT_Users objPtUsers = new BAL.PT_Users();
                objPtUsers = new BAL.AccountServiceManager().getUserByID(UserId);
                if (objPtUsers != null)
                {
                    userEmail = objPtUsers.Email;
                }
            }
            return userEmail;
        }
        protected void updateSubscriptionDetailsToCancel(string subscriptionId)
        {
            ParkingNexusBLL.clsParkingMonthlySubscription m = new ParkingNexusBLL.clsParkingMonthlySubscription();
            m.SubscriptionID = subscriptionId;
            DataTable dt = m.Get_SubscriptionDetails();
            if (dt != null && dt.Rows.Count > 0)
            {
                m.SubscriptionID = m.SubscriptionID;
                m.BillingPeriodEndDate = Convert.ToDateTime(dt.Rows[0]["BillingPeriodEndDate"]);
                m.BillingPeriodStartDate = Convert.ToDateTime(dt.Rows[0]["BillingPeriodStartDate"]);
                m.CurrentBillingCycle = Convert.ToInt32(dt.Rows[0]["CurrentBillingCycle"]);
                m.DaysPastDue = Convert.ToInt32(dt.Rows[0]["DaysPastDue"]);
                m.FailureCount = Convert.ToInt32(dt.Rows[0]["FailureCount"]);
                m.NextBillAmount = Convert.ToDecimal(dt.Rows[0]["NextBillAmount"]);
                m.NextBillingDate = Convert.ToDateTime(dt.Rows[0]["NextBillingDate"]);
                m.Price = Convert.ToDecimal(dt.Rows[0]["Price"]);
                m.isMultipleInOut = Convert.ToBoolean(dt.Rows[0]["isMultipleInOut"]);
                m.multipleInOutPrice = Convert.ToDecimal(dt.Rows[0]["multipleInOutPrice"]);
                m.ParkingTax = Convert.ToDecimal(dt.Rows[0]["ParkingTax"]);
                m.TransactionFee = Convert.ToDecimal(dt.Rows[0]["TransactionFee"]);
                m.Status = SubscriptionStatus.CANCELED.ToString();
                m.UpdatedAt = DateTime.Now;
                m.carID = Convert.ToInt32(dt.Rows[0]["carID"]);
                m.LastBillingDate = DateTime.Now;
                m.CancellationDate = DateTime.Now;
                if (m.Status == SubscriptionStatus.CANCELED.ToString())
                {
                    DataTable dtOrder = m.Get_Subscription_Orders();
                    if (dtOrder != null && dtOrder.Rows.Count > 0)
                    {
                        for (int i = 0; i < dtOrder.Rows.Count; ++i)
                        {
                            if (("Confirmed,Pending,Processing").Contains(dtOrder.Rows[i]["OrderStatus"].ToString()))
                            {
                                m.ActiveTill = Convert.ToDateTime(dtOrder.Rows[i]["REQUESTEDTODATE"] + " " + dtOrder.Rows[i]["REQUESTEDTOTIME"]);
                                break;
                            }
                        }
                    }
                }
                m.ID = m.UPDATE_ParkingMonthlySubscription();
                objUtility.sendSubscriptionMail(m);
            }
        }
    }
}