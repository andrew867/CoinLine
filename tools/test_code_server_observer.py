import queue
import socket
import threading
import unittest

from code_server_observer import observe


class CodeServerObserverTests(unittest.TestCase):
    def start(self, **overrides):
        events = queue.Queue()
        settings = dict(port=0, duration=1.5, session_duration=1.0, idle=0.4,
                        byte_limit=1024, emit=events.put)
        settings.update(overrides)
        thread = threading.Thread(target=observe, kwargs=settings, daemon=True)
        thread.start()
        ready = events.get(timeout=2)
        self.assertEqual(ready['event'], 'listening')
        self.assertEqual(ready['address'], '127.0.0.1')
        self.addCleanup(thread.join, 3)
        return events, ready['port']

    def test_receives_without_sending_and_keeps_payload_out_of_report(self):
        events, port = self.start()
        with socket.create_connection(('127.0.0.1', port), timeout=1) as client:
            self.assertEqual(events.get(timeout=1)['event'], 'connected')
            client.sendall(b'C')
            observed = events.get(timeout=1)
            self.assertEqual(observed['possibleControl'], 'possible_crc_request')
            self.assertEqual(observed['bytes'], 1)
            client.settimeout(0.05)
            with self.assertRaises(socket.timeout):
                client.recv(1)
            private_sample = b'synthetic private value'
            client.sendall(private_sample)
            client.shutdown(socket.SHUT_WR)
            received = [observed]
            while True:
                event = events.get(timeout=1)
                received.append(event)
                if event['event'] == 'closed':
                    break
            self.assertEqual(event['reason'], 'peer_closed')
            self.assertEqual(event['receivedBytes'], 1 + len(private_sample))
            self.assertEqual(event['sentBytes'], 0)
            self.assertNotIn(private_sample.decode(), repr(received))
            self.assertEqual(client.recv(1), b'')

    def test_idle_timeout_and_byte_limit(self):
        events, port = self.start(byte_limit=3)
        with socket.create_connection(('127.0.0.1', port), timeout=1):
            self.assertEqual(events.get(timeout=1)['event'], 'connected')
            event = events.get(timeout=1)
            self.assertEqual(event['reason'], 'idle_timeout')
            self.assertEqual(event['sentBytes'], 0)
        with socket.create_connection(('127.0.0.1', port), timeout=1) as client:
            self.assertEqual(events.get(timeout=1)['event'], 'connected')
            client.sendall(b'abc')
            self.assertEqual(events.get(timeout=1)['bytes'], 3)
            event = events.get(timeout=1)
            self.assertEqual(event['reason'], 'byte_limit')
            self.assertEqual(event['receivedBytes'], 3)
            self.assertEqual(event['sentBytes'], 0)


if __name__ == '__main__':
    unittest.main()
