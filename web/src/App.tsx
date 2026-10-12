import { Navigate, Route, Routes } from "react-router-dom";
import { HomePage } from "./features/home/HomePage";

export function App() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
