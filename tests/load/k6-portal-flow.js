import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Trend, Counter, Rate } from 'k6/metrics';

// Custom OpenTelemetry & SLA Performance Metrics
const loginTrend = new Trend('portal_login_duration_ms');
const bookingTrend = new Trend('portal_booking_duration_ms');
const messagingTrend = new Trend('portal_messaging_duration_ms');
const successRate = new Rate('portal_transaction_success_rate');
const bookingsCounter = new Counter('portal_appointments_booked_total');
const messagesCounter = new Counter('portal_messages_sent_total');

// Target Base URL (defaults to localhost:5000 or cluster proxy)
const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';

export const options = {
  stages: [
    { duration: '15s', target: 5 },   // Warm-up ramp
    { duration: '45s', target: 20 },  // Steady-state production concurrency
    { duration: '15s', target: 40 },  // Peak surge load test
    { duration: '15s', target: 0 },   // Graceful cool-down
  ],
  thresholds: {
    // 95% of all requests must complete within 500ms; 99% within 1000ms
    http_req_duration: ['p(95)<500', 'p(99)<1000'],
    // Error rate must remain strictly below 1% under load
    http_req_failed: ['rate<0.01'],
    // Custom transaction success SLA
    portal_transaction_success_rate: ['rate>0.99'],
  },
};

// Seed credentials
const CREDENTIALS = [
  { email: 'dana@example.com', password: 'ChangeMe!Demo2024' },
  { email: 'apatel@clinic.example', password: 'ChangeMe!Demo2024' },
];

export default function () {
  const user = CREDENTIALS[__VU % CREDENTIALS.length];
  let authToken = null;
  let targetPatientId = null;
  let providerId = null;

  // =========================================================================
  // 1. LOGIN WORKFLOW
  // =========================================================================
  group('01_User_Login', function () {
    const loginPayload = JSON.stringify({
      email: user.email,
      password: user.password,
    });

    const loginParams = {
      headers: {
        'Content-Type': 'application/json',
        'User-Agent': 'k6-load-test/1.0',
      },
    };

    const loginRes = http.post(`${BASE_URL}/api/auth/login`, loginPayload, loginParams);
    loginTrend.add(loginRes.timings.duration);

    const loginOk = check(loginRes, {
      'login status is 200': (r) => r.status === 200,
      'login returns access token': (r) => {
        try {
          const body = r.json();
          return body && body.accessToken !== undefined;
        } catch (_) {
          return false;
        }
      },
    });

    successRate.add(loginOk);

    if (loginOk) {
      const data = loginRes.json();
      authToken = data.accessToken;
      // Extract patient or person ID
      if (data.user && data.user.personId) {
        targetPatientId = data.user.patientId || data.user.personId;
      }
    }
  });

  if (!authToken) {
    sleep(1);
    return;
  }

  const authHeaders = {
    headers: {
      Authorization: `Bearer ${authToken}`,
      'Content-Type': 'application/json',
      'x-patient-id': targetPatientId || '00000000-0000-0000-0000-000000000001',
    },
  };

  // =========================================================================
  // 2. APPOINTMENT BOOKING WORKFLOW
  // =========================================================================
  group('02_Appointment_Booking', function () {
    // 2a. Fetch existing appointments / providers
    const listRes = http.get(`${BASE_URL}/api/appointments`, authHeaders);
    const listOk = check(listRes, {
      'appointments list is 200': (r) => r.status === 200,
    });
    successRate.add(listOk);

    // 2b. Request a new appointment slot (offset by random future window)
    const randomDays = Math.floor(Math.random() * 60) + 10;
    const randomHour = Math.floor(Math.random() * 6) + 9; // 9:00 - 15:00
    const startAt = new Date();
    startAt.setDate(startAt.getDate() + randomDays);
    startAt.setHours(randomHour, 0, 0, 0);

    const endAt = new Date(startAt.getTime() + 30 * 60000); // 30 min duration

    const bookingPayload = JSON.stringify({
      startAt: startAt.toISOString(),
      endAt: endAt.toISOString(),
      reasonForVisit: 'Routine Preventive Wellness Follow-Up',
      patientNotes: 'Load test automated verification booking.',
    });

    const bookRes = http.post(`${BASE_URL}/api/appointments`, bookingPayload, authHeaders);
    bookingTrend.add(bookRes.timings.duration);

    // In a multi-user load test, both 201 Created and 409 Conflict (slot collision) are expected and valid
    const bookingValid = check(bookRes, {
      'booking returns 201 Created or 409 Conflict': (r) => r.status === 201 || r.status === 409,
    });

    successRate.add(bookingValid);
    if (bookRes.status === 201) {
      bookingsCounter.add(1);
    }
  });

  // =========================================================================
  // 3. SECURE MESSAGING WORKFLOW
  // =========================================================================
  group('03_Secure_Messaging', function () {
    // 3a. Retrieve message threads
    const threadsRes = http.get(`${BASE_URL}/api/messages`, authHeaders);
    const threadsOk = check(threadsRes, {
      'messages list returns 200': (r) => r.status === 200,
    });
    successRate.add(threadsOk);

    // 3b. Send secure message to care team
    const msgPayload = JSON.stringify({
      subject: `Medication question from VU ${__VU} at ${new Date().toISOString()}`,
      body: 'Hello Dr. Patel, confirming our upcoming schedule and test results. Thank you.',
    });

    const msgRes = http.post(`${BASE_URL}/api/messages`, msgPayload, authHeaders);
    messagingTrend.add(msgRes.timings.duration);

    const msgOk = check(msgRes, {
      'message sending returns 201 or 200': (r) => r.status === 201 || r.status === 200,
    });

    successRate.add(msgOk);
    if (msgRes.status === 201 || msgRes.status === 200) {
      messagesCounter.add(1);
    }
  });

  // Realistic human think-time pacing between iterations
  sleep(1);
}
